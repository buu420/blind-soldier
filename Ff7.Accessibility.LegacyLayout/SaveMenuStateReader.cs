using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public enum SaveMenuPage
{
    SaveFiles = 0,
    Games = 1,
    Checking = 2,
    CheckingComplete = 3,
    Saving = 4,
    Confirmation = 7
}

public readonly record struct SaveMenuStateSnapshot(
    SaveMenuPage Page,
    int SaveFileNumber,
    int GameNumber,
    Ff7SaveSlotPreview? Preview,
    int ConfirmationCursor);

/// <summary>
/// Reads the native save-menu state machine identified at FUN_006FEDB0.
/// Mode 1 is the in-game Save path. Title Continue is a separate state machine
/// rooted at 0x00DD7704 and is read by <see cref="TitleLoadMenuDataReader"/>.
/// </summary>
public sealed class SaveMenuStateReader
{
    public const int SaveMode = 1;
    public const int AddressMode = 0x00DCA020;
    public const int AddressGameCursor = 0x00DC6B1C;
    public const int AddressGameScroll = 0x00DC6B2C;
    public const int AddressGameScrollState = 0x00DC6B48;
    public const int AddressConfirmationCursor = 0x00DC6C6C;
    public const int AddressOccupancyMask = 0x00DC1134;
    public const int AddressSlotBufferPointer = 0x00DD7700;
    public const int AddressSlotPreviewCache = 0x00DD6FD0;
    public const int AddressPage = 0x00DCA028;
    public const int AddressSaveFileWidget = 0x00DC6AE0;
    public const int AddressGameWidget = 0x00DC6B18;
    public const int AddressConfirmationWidget = 0x00DC6C68;

    private const ushort ValidOccupancyMask = (1 << Ff7PcSaveFileReader.SlotsPerFile) - 1;
    private readonly ILegacyAddressSpace addressSpace;

    public SaveMenuStateReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public bool TryRead(out SaveMenuStateSnapshot snapshot) =>
        TryRead(out snapshot, out _);

    public bool TryRead(
        out SaveMenuStateSnapshot snapshot,
        out string diagnostic) =>
        TryReadCore(allowTranslatedModeZero: false, out snapshot, out diagnostic);

    /// <summary>
    /// Reads the translated Steam 2026 Save state after an exact 5-by-2 Save
    /// widget has independently established ownership. The translated x64
    /// runtime leaves the legacy mode word at zero while this native widget is
    /// active, so mode zero is accepted only through this ownership-gated API.
    /// </summary>
    public bool TryReadForActiveSaveWidget(
        out SaveMenuStateSnapshot snapshot,
        out string diagnostic) =>
        TryReadCore(allowTranslatedModeZero: true, out snapshot, out diagnostic);

    private bool TryReadCore(
        bool allowTranslatedModeZero,
        out SaveMenuStateSnapshot snapshot,
        out string diagnostic)
    {
        snapshot = default;
        diagnostic = string.Empty;
        if (!TryReadRaw(
                allowTranslatedModeZero,
                out var candidate,
                out var initialDiagnostic))
        {
            diagnostic = $"initial state {initialDiagnostic}";
            return false;
        }

        if (!TryReadRaw(
                allowTranslatedModeZero,
                out var confirmation,
                out var confirmationDiagnostic))
        {
            diagnostic = $"confirmation state {confirmationDiagnostic}";
            return false;
        }

        if (candidate != confirmation)
        {
            diagnostic = $"confirmation state changed: before {FormatRaw(candidate)}; " +
                $"after {FormatRaw(confirmation)}";
            return false;
        }

        var gameNumber = 0;
        Ff7SaveSlotPreview? preview = null;
        if (candidate.Page is SaveMenuPage.Games or SaveMenuPage.Confirmation)
        {
            var selectedSlot = (long)candidate.GameScroll + candidate.GameCursor;
            if (candidate.GameCursor is < 0 or > 3 ||
                candidate.GameScroll is < 0 or >= Ff7PcSaveFileReader.SlotsPerFile ||
                selectedSlot is < 0 or >= Ff7PcSaveFileReader.SlotsPerFile)
            {
                diagnostic = $"selected game slot out of range: cursor={candidate.GameCursor} " +
                    $"scroll={candidate.GameScroll} selected={selectedSlot}";
                return false;
            }

            gameNumber = (int)selectedSlot + 1;
            if (candidate.Page == SaveMenuPage.Games)
            {
                var occupied = (candidate.OccupancyMask & (1 << (int)selectedSlot)) != 0;
                if (!occupied)
                {
                    preview = Ff7SaveSlotPreview.Empty;
                }
                else if (!TryReadOccupiedSlot(
                    (int)selectedSlot,
                    out var occupiedPreview,
                    out var previewDiagnostic))
                {
                    diagnostic = $"occupied preview {previewDiagnostic}";
                    return false;
                }
                else
                {
                    preview = occupiedPreview;
                }
            }
        }

        if (!TryReadRaw(
                allowTranslatedModeZero,
                out var bookend,
                out var bookendDiagnostic))
        {
            diagnostic = $"bookend state {bookendDiagnostic}";
            return false;
        }

        if (bookend != confirmation)
        {
            diagnostic = $"bookend state changed: before {FormatRaw(confirmation)}; " +
                $"after {FormatRaw(bookend)}";
            return false;
        }

        snapshot = new SaveMenuStateSnapshot(
            candidate.Page,
            candidate.SaveFileNumber,
            gameNumber,
            preview,
            candidate.Page == SaveMenuPage.Confirmation
                ? candidate.ConfirmationCursor
                : 0);
        diagnostic = $"active page={candidate.Page} saveFile={candidate.SaveFileNumber} " +
            $"game={gameNumber} confirmation={snapshot.ConfirmationCursor} mode={candidate.Mode}";
        return true;
    }

    private bool TryReadRaw(
        bool allowTranslatedModeZero,
        out RawSaveMenuState state,
        out string diagnostic)
    {
        state = default;
        diagnostic = string.Empty;
        if (!addressSpace.TryReadInt32((uint)AddressMode, out var mode))
        {
            diagnostic = $"mode unreadable at 0x{AddressMode:X8}";
            return false;
        }

        if (mode != SaveMode && !(allowTranslatedModeZero && mode == 0))
        {
            diagnostic = allowTranslatedModeZero
                ? $"mode mismatch: expected=0 or {SaveMode} actual={mode}"
                : $"mode mismatch: expected={SaveMode} actual={mode}";
            return false;
        }

        if (!addressSpace.TryReadInt32((uint)AddressPage, out var rawPage))
        {
            diagnostic = $"page unreadable at 0x{AddressPage:X8}";
            return false;
        }

        if (!TryNormalizePage(rawPage, out var page))
        {
            diagnostic = $"page unsupported: actual={rawPage}";
            return false;
        }

        if (!addressSpace.TryReadInt32((uint)AddressSaveFileWidget, out var fileFirst))
        {
            diagnostic = $"save-file first selector unreadable at 0x{AddressSaveFileWidget:X8}";
            return false;
        }

        if (!addressSpace.TryReadInt32(
            (uint)(AddressSaveFileWidget + 0x04),
            out var fileCursor))
        {
            diagnostic = $"save-file cursor selector unreadable at 0x{AddressSaveFileWidget + 0x04:X8}";
            return false;
        }

        if (fileFirst is < 0 or >= 5 || fileCursor is < 0 or >= 2)
        {
            diagnostic = $"save-file selectors out of range: first={fileFirst} cursor={fileCursor}";
            return false;
        }

        var gameCursor = 0;
        var gameScroll = 0;
        var gameScrollState = 0;
        var confirmationCursor = 0;
        ushort occupancyMask = 0;
        if (page is SaveMenuPage.Games or SaveMenuPage.Confirmation)
        {
            if (!addressSpace.TryReadInt32((uint)AddressGameCursor, out gameCursor))
            {
                diagnostic = $"game cursor unreadable at 0x{AddressGameCursor:X8}";
                return false;
            }

            if (!addressSpace.TryReadInt32((uint)AddressGameScroll, out gameScroll))
            {
                diagnostic = $"game scroll unreadable at 0x{AddressGameScroll:X8}";
                return false;
            }

            if (!addressSpace.TryReadInt32((uint)AddressGameScrollState, out gameScrollState))
            {
                diagnostic = $"game scroll state unreadable at 0x{AddressGameScrollState:X8}";
                return false;
            }

            if (gameCursor is < 0 or > 3 ||
                gameCursor >= 3 + (gameScrollState != 0 ? 1 : 0) ||
                gameScroll is < 0 or >= Ff7PcSaveFileReader.SlotsPerFile)
            {
                diagnostic = $"game selectors out of range: cursor={gameCursor} " +
                    $"scroll={gameScroll} scrollState={gameScrollState}";
                return false;
            }

            if (page == SaveMenuPage.Games &&
                !addressSpace.TryReadUInt16((uint)AddressOccupancyMask, out occupancyMask))
            {
                diagnostic = $"occupancy mask unreadable at 0x{AddressOccupancyMask:X8}";
                return false;
            }

            if (page == SaveMenuPage.Games &&
                (occupancyMask & ~ValidOccupancyMask) != 0)
            {
                diagnostic = $"occupancy mask out of range: actual=0x{occupancyMask:X4}";
                return false;
            }
        }

        if (page == SaveMenuPage.Confirmation)
        {
            if (!addressSpace.TryReadInt32(
                (uint)AddressConfirmationCursor,
                out confirmationCursor))
            {
                diagnostic = $"confirmation cursor unreadable at 0x{AddressConfirmationCursor:X8}";
                return false;
            }

            if (confirmationCursor is < 0 or > 1)
            {
                diagnostic = $"confirmation cursor out of range: actual={confirmationCursor}";
                return false;
            }
        }

        state = new RawSaveMenuState(
            mode,
            page,
            fileCursor * 5 + fileFirst + 1,
            gameCursor,
            gameScroll,
            gameScrollState,
            confirmationCursor,
            occupancyMask);
        return true;
    }

    private bool TryReadOccupiedSlot(
        int slotIndex,
        out Ff7SaveSlotPreview preview,
        out string diagnostic)
    {
        preview = default;
        diagnostic = string.Empty;
        if (slotIndex is < 0 or >= Ff7PcSaveFileReader.SlotsPerFile)
        {
            diagnostic = $"slot out of range: actual={slotIndex}";
            return false;
        }

        var address = (ulong)(uint)AddressSlotPreviewCache +
            (ulong)(uint)slotIndex * Ff7PcSaveFileReader.RuntimePreviewSize;
        if (address > uint.MaxValue ||
            address + Ff7PcSaveFileReader.RuntimePreviewSize > (ulong)uint.MaxValue + 1)
        {
            diagnostic = $"address out of range: slot={slotIndex} address=0x{address:X}";
            return false;
        }

        var before = new byte[Ff7PcSaveFileReader.RuntimePreviewSize];
        var after = new byte[Ff7PcSaveFileReader.RuntimePreviewSize];
        if (!addressSpace.TryRead((uint)address, before))
        {
            diagnostic = $"first read failed: slot={slotIndex} address=0x{address:X8}";
            return false;
        }

        if (!addressSpace.TryRead((uint)address, after))
        {
            diagnostic = $"confirmation read failed: slot={slotIndex} address=0x{address:X8}";
            return false;
        }

        if (!before.AsSpan().SequenceEqual(after))
        {
            diagnostic = $"changed between reads: slot={slotIndex} address=0x{address:X8}";
            return false;
        }

        if (!Ff7PcSaveFileReader.TryParseRuntimePreview(before, out preview))
        {
            diagnostic = $"parse failed: slot={slotIndex} address=0x{address:X8}";
            return false;
        }

        if (preview.IsEmpty)
        {
            diagnostic = $"parsed empty: occupied slot={slotIndex} address=0x{address:X8}";
            return false;
        }

        return true;
    }

    private static string FormatRaw(RawSaveMenuState state) =>
        $"mode={state.Mode} page={state.Page} saveFile={state.SaveFileNumber} " +
        $"gameCursor={state.GameCursor} gameScroll={state.GameScroll} " +
        $"gameScrollState={state.GameScrollState} " +
        $"confirmation={state.ConfirmationCursor} occupancy=0x{state.OccupancyMask:X4}";

    private static bool TryNormalizePage(int value, out SaveMenuPage page)
    {
        page = (SaveMenuPage)value;
        return page is SaveMenuPage.SaveFiles or
            SaveMenuPage.Games or
            SaveMenuPage.Checking or
            SaveMenuPage.CheckingComplete or
            SaveMenuPage.Saving or
            SaveMenuPage.Confirmation;
    }

    private readonly record struct RawSaveMenuState(
        int Mode,
        SaveMenuPage Page,
        int SaveFileNumber,
        int GameCursor,
        int GameScroll,
        int GameScrollState,
        int ConfirmationCursor,
        ushort OccupancyMask);
}
