namespace Ff7.Accessibility.Reloaded;

public sealed class MainMenuStateReader
{
    public const int AddressState = 0x00DC1294;
    public const int AddressSelectedA = 0x00DC1208;
    public const int AddressSelectedB = 0x00DC1120;
    public const int AddressCursorIndex = 0x00DC1154;
    public const int AddressTarget = 0x00DC12EC;
    public const int AddressOpenFlag = 0x00DC1108;
    public const int AddressEnabledMask = 0x00DC111C;
    public const int AddressDisabledMask = 0x00DC1130;
    public const int AddressAnimation = 0x0091AB04;

    public static readonly string[] Labels =
    [
        "Item",
        "Magic",
        "Materia",
        "Equip",
        "Status",
        "Order",
        "Limit",
        "Config",
        "PHS",
        "Save",
        "Quit"
    ];

    private readonly Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace addressSpace;

    public MainMenuStateReader(Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public bool TryReadSnapshot(out MainMenuSnapshot snapshot)
    {
        snapshot = default;
        if (!TryReadFields(out var candidate) ||
            !TryReadFields(out var bookend) ||
            candidate != bookend)
        {
            return false;
        }

        snapshot = candidate;
        return true;
    }

    private bool TryReadFields(out MainMenuSnapshot snapshot)
    {
        snapshot = default;
        if (!TryReadInt32(AddressState, out var state) ||
            !TryReadInt32(AddressSelectedA, out var selectedA) ||
            !TryReadInt32(AddressSelectedB, out var selectedB) ||
            !TryReadInt32(AddressCursorIndex, out var cursorIndex) ||
            !TryReadInt32(AddressTarget, out var target) ||
            !TryReadInt32(AddressOpenFlag, out var menuOpen) ||
            !TryReadUInt32(AddressEnabledMask, out var enabledMask) ||
            !TryReadUInt32(AddressDisabledMask, out var disabledMask) ||
            !TryReadInt32(AddressAnimation, out var animation))
        {
            return false;
        }

        snapshot = new MainMenuSnapshot(
            state,
            selectedA,
            selectedB,
            cursorIndex,
            target,
            menuOpen,
            enabledMask,
            disabledMask,
            animation);
        return true;
    }

    public static bool TryCreateSelection(MainMenuSnapshot snapshot, out MainMenuSelection selection)
    {
        selection = default;
        if (snapshot.MenuOpen == 0)
        {
            return false;
        }

        if (snapshot.State < 0 || snapshot.State > 6)
        {
            return false;
        }

        if ((snapshot.EnabledMask & 0x7ffu) == 0)
        {
            return false;
        }

        var index = snapshot.State == 1
            ? snapshot.CursorIndex
            : snapshot.State is >= 4 and <= 6
                ? snapshot.SelectedB
                : snapshot.SelectedA;
        if (index < 0 || index >= Labels.Length)
        {
            return false;
        }

        var label = Labels[index];
        var bit = 1u << index;
        if ((snapshot.EnabledMask & bit) == 0)
        {
            // The cursor fields can retain an index while that row is not drawn.  Do not
            // turn a hidden row into speech from the static label table.
            return false;
        }

        var isAvailable = (snapshot.DisabledMask & bit) == 0;
        selection = new MainMenuSelection(
            index,
            label,
            isAvailable ? label : $"{label} unavailable",
            isAvailable,
            snapshot.State,
            snapshot.EnabledMask,
            snapshot.DisabledMask);
        return true;
    }

    private bool TryReadInt32(int address, out int value) =>
        Ff7.Accessibility.LegacyLayout.LegacyAddressSpaceExtensions.TryReadInt32(
            addressSpace,
            (uint)address,
            out value);

    private bool TryReadUInt32(int address, out uint value) =>
        Ff7.Accessibility.LegacyLayout.LegacyAddressSpaceExtensions.TryReadUInt32(
            addressSpace,
            (uint)address,
            out value);
}

public readonly record struct MainMenuSnapshot(
    int State,
    int SelectedA,
    int SelectedB,
    int CursorIndex,
    int Target,
    int MenuOpen,
    uint EnabledMask,
    uint DisabledMask,
    int Animation);

public readonly record struct MainMenuSelection(
    int Index,
    string Label,
    string SpokenText,
    bool IsAvailable,
    int State,
    uint EnabledMask,
    uint DisabledMask);
