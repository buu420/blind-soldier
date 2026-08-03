using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public enum TitleLoadMenuPage
{
    SaveFiles = 0,
    Games = 1,
    Checking = 2,
    CheckingComplete = 3,
    Loading = 4,
    TitleRoot = 7
}

public readonly record struct TitleLoadMenuStateSnapshot(
    TitleLoadMenuPage Page,
    int SaveFileNumber,
    bool SaveFileHasData,
    int GameNumber,
    Ff7SaveSlotPreview? Preview);

/// <summary>
/// Reads the distinct title Continue state machine at FUN_007212FB. Save data
/// details come from the same bounded preview cache used by the sighted UI.
/// </summary>
public sealed class TitleLoadMenuDataReader
{
    public const int InteractiveReadiness = 1;
    public const int GamePage = 1;
    public const int AddressReadiness = 0x00DD74E0;
    public const int AddressPage = 0x00DD7704;
    public const int AddressSaveFileWidget = 0x00DD6D98;
    public const int AddressGameWidget = 0x00DD6DD0;
    public const int AddressSaveFileAvailability = 0x00DD75E8;
    public const int SaveFileAvailabilityStride = 3;

    private const ushort ValidOccupancyMask =
        (1 << Ff7PcSaveFileReader.SlotsPerFile) - 1;
    private readonly Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace addressSpace;
    private readonly ActiveMenuWidgetReader widgetReader;

    public TitleLoadMenuDataReader(
        Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
        widgetReader = new ActiveMenuWidgetReader(addressSpace);
    }

    public bool TryRead(out TitleLoadMenuStateSnapshot snapshot)
    {
        snapshot = default;
        if (!TryReadRaw(out var candidate) ||
            !TryReadRaw(out var confirmation) ||
            candidate != confirmation)
        {
            return false;
        }

        Ff7SaveSlotPreview? preview = null;
        if (candidate.Page == TitleLoadMenuPage.Games)
        {
            var slotIndex = candidate.GameNumber - 1;
            var occupied = (candidate.OccupancyMask & (1 << slotIndex)) != 0;
            if (!occupied)
            {
                preview = Ff7SaveSlotPreview.Empty;
            }
            else if (!TryReadOccupiedPreview(slotIndex, out var occupiedPreview))
            {
                return false;
            }
            else
            {
                preview = occupiedPreview;
            }
        }

        if (!TryReadRaw(out var bookend) || bookend != confirmation)
        {
            return false;
        }

        snapshot = new TitleLoadMenuStateSnapshot(
            candidate.Page,
            candidate.SaveFileNumber,
            candidate.SaveFileHasData,
            candidate.GameNumber,
            preview);
        return true;
    }

    public bool? HasData(int saveFileNumber)
    {
        if (!TryGetAvailabilityAddress(saveFileNumber, out var address) ||
            !addressSpace.TryReadByte(address, out var before) ||
            !addressSpace.TryReadByte(address, out var after) ||
            before != after)
        {
            return null;
        }

        return before != 0;
    }

    public Ff7SaveSlotPreview? ReadSlot(int saveFileNumber, int gameNumber)
    {
        if (gameNumber is < 1 or > Ff7PcSaveFileReader.SlotsPerFile ||
            !TryReadRaw(out var candidate) ||
            candidate.Page != TitleLoadMenuPage.Games ||
            candidate.SaveFileNumber != saveFileNumber)
        {
            return null;
        }

        var slotIndex = gameNumber - 1;
        Ff7SaveSlotPreview preview;
        if ((candidate.OccupancyMask & (1 << slotIndex)) == 0)
        {
            preview = Ff7SaveSlotPreview.Empty;
        }
        else if (!TryReadOccupiedPreview(slotIndex, out preview))
        {
            return null;
        }

        return TryReadRaw(out var bookend) && bookend == candidate
            ? preview
            : null;
    }

    private bool TryReadRaw(out RawTitleLoadState state)
    {
        state = default;
        if (!addressSpace.TryReadInt32((uint)AddressReadiness, out var readiness) ||
            readiness != InteractiveReadiness ||
            !addressSpace.TryReadInt32((uint)AddressPage, out var rawPage) ||
            !TryNormalizePage(rawPage, out var page))
        {
            return false;
        }

        if (page == TitleLoadMenuPage.TitleRoot)
        {
            state = new RawTitleLoadState(page, 0, false, 0, 0);
            return true;
        }

        if (!widgetReader.TryRead(AddressSaveFileWidget, out var fileWidget) ||
            !IsVerifiedSaveFileGrid(fileWidget) ||
            fileWidget.First is < 0 || fileWidget.First >= fileWidget.Columns ||
            fileWidget.Cursor is < 0 || fileWidget.Cursor >= fileWidget.Rows)
        {
            return false;
        }

        var saveFileNumber = fileWidget.Cursor * fileWidget.Columns + fileWidget.First + 1;
        if (!TryGetAvailabilityAddress(saveFileNumber, out var availabilityAddress) ||
            !addressSpace.TryReadByte(availabilityAddress, out var availability))
        {
            return false;
        }

        var gameNumber = 0;
        ushort occupancyMask = 0;
        if (page == TitleLoadMenuPage.Games)
        {
            if (!widgetReader.TryRead(AddressGameWidget, out var gameWidget) ||
                gameWidget.Columns != 1 || gameWidget.Rows is not (3 or 4) ||
                gameWidget.Cursor is < 0 or > 3 ||
                gameWidget.Cursor >= 3 + (gameWidget.ScrollState != 0 ? 1 : 0) ||
                gameWidget.ScrollOffset is < 0 or >= Ff7PcSaveFileReader.SlotsPerFile ||
                (long)gameWidget.Cursor + gameWidget.ScrollOffset >= Ff7PcSaveFileReader.SlotsPerFile ||
                !addressSpace.TryReadUInt16(
                    (uint)SaveMenuStateReader.AddressOccupancyMask,
                    out occupancyMask) ||
                (occupancyMask & ~ValidOccupancyMask) != 0)
            {
                return false;
            }

            gameNumber = gameWidget.ScrollOffset + gameWidget.Cursor + 1;
        }

        state = new RawTitleLoadState(
            page,
            saveFileNumber,
            availability != 0,
            gameNumber,
            occupancyMask);
        return true;
    }

    private bool TryReadOccupiedPreview(int slotIndex, out Ff7SaveSlotPreview preview)
    {
        preview = default;
        if (slotIndex is < 0 or >= Ff7PcSaveFileReader.SlotsPerFile)
        {
            return false;
        }

        var address = (ulong)(uint)SaveMenuStateReader.AddressSlotPreviewCache +
            (ulong)(uint)slotIndex * Ff7PcSaveFileReader.RuntimePreviewSize;
        if (address > uint.MaxValue ||
            address + Ff7PcSaveFileReader.RuntimePreviewSize > (ulong)uint.MaxValue + 1)
        {
            return false;
        }

        var before = new byte[Ff7PcSaveFileReader.RuntimePreviewSize];
        var after = new byte[Ff7PcSaveFileReader.RuntimePreviewSize];
        return addressSpace.TryRead((uint)address, before) &&
            addressSpace.TryRead((uint)address, after) &&
            before.AsSpan().SequenceEqual(after) &&
            Ff7PcSaveFileReader.TryParseRuntimePreview(before, out preview) &&
            !preview.IsEmpty;
    }

    private static bool TryGetAvailabilityAddress(int saveFileNumber, out uint address)
    {
        address = 0;
        if (saveFileNumber is < 1 or > 10)
        {
            return false;
        }

        var candidate = (ulong)(uint)AddressSaveFileAvailability +
            (ulong)(uint)(saveFileNumber - 1) * SaveFileAvailabilityStride;
        if (candidate > uint.MaxValue)
        {
            return false;
        }

        address = (uint)candidate;
        return true;
    }

    private static bool IsVerifiedSaveFileGrid(ActiveMenuWidgetSnapshot widget) =>
        (widget.Columns == 5 && widget.Rows == 2) ||
        (widget.Columns == 2 && widget.Rows == 5);

    private static bool TryNormalizePage(int value, out TitleLoadMenuPage page)
    {
        page = (TitleLoadMenuPage)value;
        return page is TitleLoadMenuPage.SaveFiles or
            TitleLoadMenuPage.Games or
            TitleLoadMenuPage.Checking or
            TitleLoadMenuPage.CheckingComplete or
            TitleLoadMenuPage.Loading or
            TitleLoadMenuPage.TitleRoot;
    }

    private readonly record struct RawTitleLoadState(
        TitleLoadMenuPage Page,
        int SaveFileNumber,
        bool SaveFileHasData,
        int GameNumber,
        ushort OccupancyMask);
}
