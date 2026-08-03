using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Menus;

internal readonly record struct Steam2026NativeTitleMenuSelection(
    int Index,
    string Text,
    string Key);

/// <summary>
/// Reads the exact four-row title widget constructed by the supported Steam
/// 2026 executable. Ghidra maps guest 0x00720E64 to host RVA 0x12401D0 and
/// shows that constructor creating guest widget 0x00DD6F20 as one column by
/// four rows. The title loop reads its selected row from widget + 4.
/// </summary>
internal sealed class Steam2026NativeTitleMenuReader
{
    internal const byte TitleModule = 20;
    internal const int RowCount = 4;
    internal const uint CurrentModuleAddress = FieldPositionReader.AddressCurrentModule;
    internal const uint WidgetAddress = 0x00DD6F20;
    internal const uint TitleStateAddress = 0x00DD7704;
    internal const uint InputActiveAddress = 0x00DD74E0;
    internal const uint ExitStateAddress = 0x00DD7738;

    private static readonly string[] RowLabels =
    [
        "New Game",
        "Continue?",
        "Additional Credits",
        "Quit"
    ];

    private readonly ILegacyAddressSpace addressSpace;
    private readonly ActiveMenuWidgetReader widgetReader;

    internal Steam2026NativeTitleMenuReader(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory)
        : this(ValidatedTranslatedX86AddressSpaceFactory.Create(
            fingerprint,
            moduleBase,
            memory))
    {
    }

    internal Steam2026NativeTitleMenuReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        widgetReader = new ActiveMenuWidgetReader(addressSpace);
    }

    internal bool TryRead(out Steam2026NativeTitleMenuSelection selection) =>
        TryRead(out selection, out _);

    internal bool TryRead(
        out Steam2026NativeTitleMenuSelection selection,
        out string diagnostic)
    {
        selection = default;
        diagnostic = string.Empty;
        if (!TryCaptureOwner(out var ownerBefore, out diagnostic))
        {
            return false;
        }

        if (ownerBefore.Module != TitleModule
            || ownerBefore.TitleState != 7
            || ownerBefore.InputActive != 1
            || ownerBefore.ExitState != 0)
        {
            diagnostic = FormatOwner("inactive owner", ownerBefore);
            return false;
        }

        if (!widgetReader.TryRead(WidgetAddress, out var widget))
        {
            diagnostic = $"title widget unreadable or unstable at 0x{WidgetAddress:X8}";
            return false;
        }

        if (!TryCaptureOwner(out var ownerAfter, out diagnostic))
        {
            diagnostic = $"owner bookend {diagnostic}";
            return false;
        }

        if (ownerAfter != ownerBefore)
        {
            diagnostic = $"owner changed: before {FormatOwnerValues(ownerBefore)}; "
                         + $"after {FormatOwnerValues(ownerAfter)}";
            return false;
        }

        if (widget.Address != WidgetAddress
            || !string.Equals(widget.Name, "Title menu", StringComparison.Ordinal)
            || widget.Kind != MenuWidgetKind.Generic
            || widget.First != 0
            || widget.Columns != 1
            || widget.Rows != RowCount
            || widget.Cursor < 0
            || widget.Cursor >= RowLabels.Length)
        {
            diagnostic = $"title widget mismatch: address=0x{widget.Address:X8} "
                         + $"first={widget.First} cursor={widget.Cursor} "
                         + $"columns={widget.Columns} rows={widget.Rows}";
            return false;
        }

        var index = widget.Cursor;
        selection = new Steam2026NativeTitleMenuSelection(
            index,
            RowLabels[index],
            $"steam2026-title-menu\u001f{index}");
        diagnostic = $"active selection index={index} text={RowLabels[index]}";
        return true;
    }

    private bool TryCaptureOwner(
        out TitleOwnerSnapshot snapshot,
        out string diagnostic)
    {
        snapshot = default;
        diagnostic = string.Empty;
        if (!addressSpace.TryReadByte(CurrentModuleAddress, out var module))
        {
            diagnostic = $"module unreadable at 0x{CurrentModuleAddress:X8}";
            return false;
        }

        if (!addressSpace.TryReadInt32(TitleStateAddress, out var titleState))
        {
            diagnostic = $"title state unreadable at 0x{TitleStateAddress:X8}; module={module}";
            return false;
        }

        if (!addressSpace.TryReadInt32(InputActiveAddress, out var inputActive))
        {
            diagnostic = $"input state unreadable at 0x{InputActiveAddress:X8}; "
                         + $"module={module} titleState={titleState}";
            return false;
        }

        if (!addressSpace.TryReadInt32(ExitStateAddress, out var exitState))
        {
            diagnostic = $"exit state unreadable at 0x{ExitStateAddress:X8}; "
                         + $"module={module} titleState={titleState} inputActive={inputActive}";
            return false;
        }

        snapshot = new TitleOwnerSnapshot(module, titleState, inputActive, exitState);
        return true;
    }

    private static string FormatOwner(string prefix, TitleOwnerSnapshot owner) =>
        $"{prefix}: {FormatOwnerValues(owner)}";

    private static string FormatOwnerValues(TitleOwnerSnapshot owner) =>
        $"module={owner.Module} titleState={owner.TitleState} "
        + $"inputActive={owner.InputActive} exitState={owner.ExitState}";

    private readonly record struct TitleOwnerSnapshot(
        byte Module,
        int TitleState,
        int InputActive,
        int ExitState);
}
