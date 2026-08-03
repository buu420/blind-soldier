using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Menus;

/// <summary>
/// Reads and normalizes independent menu domains from the legacy guest address
/// space. This component creates no hooks, publishes no events, and owns no
/// capability or speech lifecycle.
/// </summary>
public sealed class Steam2026MenuObservationReader
{
    internal const uint CurrentStatusPartySlotAddress = 0x00DCA478;
    internal const uint SecondaryEquipmentPartySlotAddress =
        SavemapPartyReader.AddressEquipmentMenuPartySlot;

    private readonly ILegacyAddressSpace addressSpace;
    private readonly MainMenuStateReader mainMenuReader;
    private readonly QuitConfirmationStateReader quitConfirmationReader;
    private readonly ActiveMenuWidgetReader widgetReader;
    private readonly ConfigMenuValueReader configReader;
    private readonly MagicMenuSelectionReader magicReader;
    private readonly SavemapPartyReader partyReader;
    private readonly EquipmentMenuSelectionReader equipmentReader;
    private readonly InventoryItemReader inventoryReader;
    private readonly SaveMenuStateReader saveMenuReader;
    private readonly TitleLoadMenuDataReader titleLoadMenuReader;
    private readonly Func<int, string?> inventoryObjectNameResolver;
    private readonly Func<int, string?> inventoryObjectDescriptionResolver;
    private MateriaMenuSelectionReader? materiaReader;
    private ShopMenuStateReader shopReader;

    internal string LastSaveMenuDiagnostic { get; private set; } = "not sampled";

    public Steam2026MenuObservationReader(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        INativeMemoryReader memory,
        Func<int, string?> resolveMagicName,
        Func<int, string?> resolveMagicDescription,
        Func<int, string?>? resolveWeaponName = null,
        Func<int, string?>? resolveArmorName = null,
        Func<int, string?>? resolveAccessoryName = null,
        Func<int, string?>? resolveItemName = null,
        Func<int, string?>? resolveItemDescription = null,
        int savemapAddress = SavemapPartyReader.AddressSavemap,
        Func<int, string?>? resolveMateriaName = null,
        Func<int, string?>? resolveMateriaDescription = null)
        : this(
            ValidatedTranslatedX86AddressSpaceFactory.Create(
                fingerprint,
                moduleBase,
                memory),
            resolveMagicName,
            resolveMagicDescription,
            resolveWeaponName,
            resolveArmorName,
            resolveAccessoryName,
            resolveItemName,
            resolveItemDescription,
            savemapAddress)
    {
        ConfigureNativeDetailResolvers(resolveMateriaName, resolveMateriaDescription);
    }

    internal Steam2026MenuObservationReader(
        ILegacyAddressSpace addressSpace,
        Func<int, string?> resolveMagicName,
        Func<int, string?> resolveMagicDescription,
        Func<int, string?>? resolveWeaponName = null,
        Func<int, string?>? resolveArmorName = null,
        Func<int, string?>? resolveAccessoryName = null,
        Func<int, string?>? resolveItemName = null,
        Func<int, string?>? resolveItemDescription = null,
        int savemapAddress = SavemapPartyReader.AddressSavemap)
    {
        ArgumentNullException.ThrowIfNull(addressSpace);
        this.addressSpace = addressSpace;
        inventoryObjectNameResolver = resolveItemName ?? (_ => null);
        inventoryObjectDescriptionResolver = resolveItemDescription ?? (_ => null);
        mainMenuReader = new MainMenuStateReader(addressSpace);
        quitConfirmationReader = new QuitConfirmationStateReader(addressSpace);
        widgetReader = new ActiveMenuWidgetReader(addressSpace);
        configReader = new ConfigMenuValueReader(addressSpace);
        magicReader = new MagicMenuSelectionReader(
            addressSpace,
            resolveMagicName,
            resolveMagicDescription);
        partyReader = new SavemapPartyReader(
            addressSpace,
            resolveWeaponName,
            resolveArmorName,
            resolveAccessoryName,
            savemapAddress,
            inventoryObjectDescriptionResolver);
        equipmentReader = new EquipmentMenuSelectionReader(
            addressSpace,
            resolveWeaponName,
            resolveArmorName,
            resolveAccessoryName,
            inventoryObjectDescriptionResolver,
            savemapAddress);
        inventoryReader = new InventoryItemReader(
            addressSpace,
            inventoryObjectNameResolver,
            inventoryObjectDescriptionResolver,
            unchecked((uint)savemapAddress));
        saveMenuReader = new SaveMenuStateReader(addressSpace);
        titleLoadMenuReader = new TitleLoadMenuDataReader(addressSpace);
        shopReader = new ShopMenuStateReader(
            addressSpace,
            inventoryObjectNameResolver,
            inventoryObjectDescriptionResolver);
    }

    public bool TryReadMainMenu(out MainMenuObservationSnapshot snapshot)
    {
        snapshot = default;
        if (!mainMenuReader.TryReadSnapshot(out var state))
        {
            return false;
        }

        MainMenuSelection? selection = MainMenuStateReader.TryCreateSelection(state, out var candidate)
            ? candidate
            : null;
        snapshot = new MainMenuObservationSnapshot(state, selection);
        return true;
    }

    public bool TryReadQuitConfirmation(out QuitConfirmationSnapshot snapshot) =>
        quitConfirmationReader.TryRead(out snapshot);

    public bool TryNormalizeTitleCursor(
        TitleMenuCursorSnapshot callbackObservation,
        out TitleMenuCursorSelection selection)
    {
        // The Steam 2026 atlas verifies the cursor callback and its coordinates,
        // but coordinates do not identify the rendered row. Until a frame
        // coordinator can correlate this callback with verified native text or
        // widget evidence, exposing a title selection would be a guessed label.
        _ = callbackObservation;
        selection = default;
        return false;
    }

    public bool TryReadActiveWidget(
        uint widgetGuestAddress,
        out Steam2026MenuWidgetObservationSnapshot snapshot)
    {
        snapshot = default;
        return widgetReader.TryRead(widgetGuestAddress, out var raw)
               && TryNormalizeWidget(raw, out snapshot);
    }

    public bool TryReadConfigValue(
        string nativeRowLabel,
        out NativeMenuSelection selection)
    {
        selection = default;
        var candidate = configReader.ReadMainValue(nativeRowLabel);
        if (candidate is null)
        {
            return false;
        }

        selection = candidate.Value;
        return true;
    }

    public bool TryReadSoundVolume(int cursor, out NativeMenuSelection selection)
    {
        selection = default;
        var candidate = configReader.ReadSoundVolume(cursor);
        if (candidate is null)
        {
            return false;
        }

        selection = candidate.Value;
        return true;
    }

    public bool TryReadMagic(
        uint widgetGuestAddress,
        out MagicMenuObservationSnapshot snapshot)
    {
        snapshot = default;
        if (!widgetReader.TryRead(widgetGuestAddress, out var rawWidget) ||
            !magicReader.TryRead(rawWidget, out var spell) ||
            !widgetReader.TryRead(widgetGuestAddress, out var widgetBookend) ||
            widgetBookend != rawWidget ||
            !TryNormalizeWidget(rawWidget, out var widget))
        {
            return false;
        }

        snapshot = new MagicMenuObservationSnapshot(widget, spell);
        return true;
    }

    public bool TryReadPartyMember(int partySlot, out PartyMemberSnapshot snapshot)
    {
        snapshot = default;
        if (!partyReader.TryReadPartySlot(partySlot, out var candidate) ||
            !partyReader.TryReadPartySlot(partySlot, out var bookend) ||
            bookend != candidate)
        {
            return false;
        }

        snapshot = candidate;
        return true;
    }

    public bool TryReadInventoryItem(int slot, out InventoryItemSnapshot snapshot) =>
        inventoryReader.TryRead(slot, out snapshot);

    /// <summary>
    /// Reads the translated in-game Save state machine after the exact Save
    /// widget has independently established ownership. Title Continue uses a
    /// separate state machine; callers must not use this state read to acquire
    /// menu ownership because translated mode zero also exists at the root.
    /// </summary>
    public bool TryReadSaveMenu(out SaveMenuStateSnapshot snapshot)
    {
        var result = saveMenuReader.TryReadForActiveSaveWidget(
            out snapshot,
            out var diagnostic);
        LastSaveMenuDiagnostic = diagnostic;
        return result;
    }

    public bool TryReadSaveMenu(
        out SaveMenuStateSnapshot snapshot,
        out string diagnostic)
    {
        var result = saveMenuReader.TryReadForActiveSaveWidget(out snapshot, out diagnostic);
        LastSaveMenuDiagnostic = diagnostic;
        return result;
    }

    internal bool TryReadTitleLoadMenu(out TitleLoadMenuStateSnapshot snapshot) =>
        titleLoadMenuReader.TryRead(out snapshot);

    internal bool? TitleLoadSaveFileHasData(int saveFileNumber) =>
        titleLoadMenuReader.HasData(saveFileNumber);

    internal Ff7SaveSlotPreview? ReadTitleLoadGame(int saveFileNumber, int gameNumber) =>
        titleLoadMenuReader.ReadSlot(saveFileNumber, gameNumber);

    public bool TryReadEquipment(
        int partySlot,
        int equipmentSlot,
        out NativeMenuSelection selection) =>
        partyReader.TryReadEquipment(partySlot, equipmentSlot, out selection);

    public bool TryReadSecondaryEquipment(
        int equipmentSlot,
        out NativeMenuSelection selection) =>
        partyReader.TryReadSelectedEquipment(equipmentSlot, out selection);

    public bool TryReadEquipmentList(out NativeMenuSelection selection) =>
        equipmentReader.TryRead(out selection);

    public bool TryReadMateria(
        MenuWidgetKind kind,
        out NativeMenuSelection selection)
    {
        selection = default;
        return materiaReader?.TryRead(kind, out selection) == true;
    }

    internal bool TryReadShopMenuOwnership(out bool ownsShop) =>
        shopReader.TryReadOwnership(out ownsShop);

    internal bool TryReadShopMenu(out ShopMenuSnapshot snapshot) =>
        shopReader.TryRead(out snapshot);

    internal string? PollShopMenu(ShopMenuSpeechTracker tracker) =>
        tracker.Poll(shopReader);

    internal void ConfigureNativeDetailResolvers(
        Func<int, string?>? resolveMateriaName,
        Func<int, string?>? resolveMateriaDescription)
    {
        materiaReader = new MateriaMenuSelectionReader(
            addressSpace,
            resolveMateriaName,
            resolveMateriaDescription);
        shopReader = new ShopMenuStateReader(
            addressSpace,
            inventoryObjectNameResolver,
            inventoryObjectDescriptionResolver,
            resolveMateriaName,
            resolveMateriaDescription);
    }

    public bool TryReadStatusSummary(int partySlot, out StatusMenuSnapshot snapshot)
    {
        snapshot = default;
        if (!partyReader.TryReadStatusSummary(partySlot, out var candidate) ||
            !partyReader.TryReadStatusSummary(partySlot, out var bookend) ||
            bookend != candidate)
        {
            return false;
        }

        snapshot = candidate;
        return true;
    }

    /// <summary>
    /// Reads the party slot selected by the native Status screen and bookends
    /// the aggregate Status read with that selector. A character transition is
    /// therefore silence, never a summary assembled from two party members.
    /// </summary>
    public bool TryReadCurrentStatusSummary(out StatusMenuSnapshot snapshot)
    {
        snapshot = default;
        if (!LegacyAddressSpaceExtensions.TryReadInt32(
                addressSpace,
                CurrentStatusPartySlotAddress,
                out var partySlot) ||
            partySlot is < 0 or >= 3 ||
            !TryReadStatusSummary(partySlot, out var candidate) ||
            candidate.PartySlot != partySlot ||
            !LegacyAddressSpaceExtensions.TryReadInt32(
                addressSpace,
                CurrentStatusPartySlotAddress,
                out var selectorBookend) ||
            selectorBookend != partySlot)
        {
            return false;
        }

        snapshot = candidate;
        return true;
    }

    private static bool TryNormalizeWidget(
        ActiveMenuWidgetSnapshot raw,
        out Steam2026MenuWidgetObservationSnapshot snapshot)
    {
        snapshot = default;
        if (!MenuWidgetCatalog.TryResolve(raw.Address, out var descriptor) ||
            !string.Equals(raw.Name, descriptor.Name, StringComparison.Ordinal) ||
            raw.Kind != descriptor.Kind)
        {
            return false;
        }

        snapshot = new Steam2026MenuWidgetObservationSnapshot(
            descriptor.Name,
            descriptor.Kind,
            raw.First,
            raw.Cursor,
            raw.Columns,
            raw.Rows,
            raw.ScrollOffset,
            raw.ScrollDelta,
            raw.ScrollState);
        return true;
    }
}

public readonly record struct MainMenuObservationSnapshot(
    MainMenuSnapshot State,
    MainMenuSelection? Selection);

public readonly record struct Steam2026MenuWidgetObservationSnapshot(
    string VerifiedName,
    MenuWidgetKind Kind,
    int First,
    int Cursor,
    int Columns,
    int Rows,
    int ScrollOffset,
    int ScrollDelta,
    int ScrollState);

public readonly record struct MagicMenuObservationSnapshot(
    Steam2026MenuWidgetObservationSnapshot Widget,
    MagicMenuSpellSnapshot Spell);
