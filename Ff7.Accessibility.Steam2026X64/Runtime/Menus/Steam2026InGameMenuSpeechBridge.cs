using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Menus;

/// <summary>
/// Adapts validated translated-menu callbacks to the shared native in-game
/// menu coordinators. Foreground module 5 outside name entry and the exact
/// native PHS module are owned directly. World-map module 3 is provisional
/// until an exact cataloged native menu widget proves that a menu is open.
/// </summary>
internal sealed class Steam2026InGameMenuSpeechBridge
{
    internal const int MenuModule = 5;
    internal const int PhsModule = PartyFormationSpeechTracker.PhsModule;

    private const int RootMainMenuContext = 0x3A83126F;
    private const int QuitPromptContext = 0x3C23D70A;
    private const uint SecondaryEquipmentSlotWidgetIdentity = 0x00DCA5C0;
    private static readonly TimeSpan DefaultSettleTime = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan ExactQuitEvidenceWindow = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan ExactWorldMapMenuEvidenceWindow = TimeSpan.FromMilliseconds(300);
    private readonly Func<string, NativeMenuSelection?> readConfigValue;
    private readonly Func<int, NativeMenuSelection?> readSoundVolume;
    private readonly Func<int, PartyMemberSnapshot?> readPartyMember;
    private Func<uint, int, NativeMenuSelection?> readOrder = (_, _) => null;
    private readonly Func<int, StatusMenuSnapshot?> readPartyStatus;
    private readonly Func<uint, MagicMenuObservationSnapshot?> readMagic;
    private readonly Func<StatusMenuSnapshot?> readCurrentStatus;
    private readonly Func<int, InventoryItemSnapshot?> readInventoryItem;
    private Func<int, InventoryMenuSlotSnapshot?> readInventorySlot = _ => null;
    private Func<uint, AbilityMenuSlotObservationSnapshot?> readAbilitySlot = _ => null;
    private readonly Func<int, int, NativeMenuSelection?> readEquipment;
    private readonly Func<int, NativeMenuSelection?> readSecondaryEquipment;
    private readonly Func<SaveMenuStateSnapshot?> readSaveMenu;
    private Func<NativeMenuSelection?> readEquipmentList = () => null;
    private Func<MenuWidgetKind, NativeMenuSelection?> readMateria = _ => null;

    // Assigned after the constructor chain has already built the first trackers,
    // so PartyFormationSpeechTracker reads it through a deferred lambda.
    private Func<int, string?> readPhsRosterName = _ => null;
    private readonly TimeSpan settleTime;
    private ActiveMenuFrameSpeechCoordinator activeMenu = null!;
    private StaticMenuCursorSpeechTracker staticMenu = null!;
    private StatusMenuSpeechTracker statusMenu = null!;
    private MateriaTutorialSpeechTracker materiaTutorial = null!;
    private PartyFormationSpeechTracker partyFormation = null!;
    private SaveMenuSpeechTracker saveMenu = null!;
    private bool ownsMenu;
    private bool ownsWorldMapIngress;
    private int? ownedModuleId;
    private long lastSequence;
    private DateTime exactQuitEvidenceExpiresUtc = DateTime.MinValue;
    private DateTime exactWorldMapMenuEvidenceExpiresUtc = DateTime.MinValue;
    private SaveMenuPendingSpeech? pendingSaveSpeech;

    internal Steam2026InGameMenuSpeechBridge(
        Steam2026MenuObservationReader menuReader,
        TimeSpan? settleTime = null)
        : this(
            label => menuReader.TryReadConfigValue(label, out var selection) ? selection : null,
            cursor => menuReader.TryReadSoundVolume(cursor, out var selection) ? selection : null,
            partySlot => menuReader.TryReadPartyMember(partySlot, out var member) ? member : null,
            address => menuReader.TryReadMagic(address, out var magic) ? magic : null,
            () => menuReader.TryReadCurrentStatusSummary(out var status) ? status : null,
            slot => menuReader.TryReadInventoryItem(slot, out var item) ? item : null,
            (partySlot, equipmentSlot) =>
                menuReader.TryReadEquipment(partySlot, equipmentSlot, out var selection)
                    ? selection
                    : null,
            equipmentSlot =>
                menuReader.TryReadSecondaryEquipment(equipmentSlot, out var selection)
                    ? selection
                    : null,
            () => menuReader.TryReadSaveMenu(out var saveMenu) ? saveMenu : null,
            settleTime ?? DefaultSettleTime)
    {
        ArgumentNullException.ThrowIfNull(menuReader);
        readInventorySlot = slot =>
            menuReader.TryReadInventorySlot(slot, out var inventorySlot)
                ? inventorySlot
                : null;
        readAbilitySlot = address =>
            menuReader.TryReadAbilitySlot(address, out var abilitySlot)
                ? abilitySlot
                : null;
        readOrder = (widgetAddress, partySlot) =>
            menuReader.TryReadOrder(widgetAddress, partySlot, out var selection)
                ? selection
                : null;
        readPartyStatus = partySlot =>
            menuReader.TryReadStatusSummary(partySlot, out var status)
                ? status
                : null;
        readEquipmentList = () =>
            menuReader.TryReadEquipmentList(out var selection)
                ? selection
                : null;
        readMateria = kind =>
            menuReader.TryReadMateria(kind, out var selection)
                ? selection
                : null;
        readPhsRosterName = menuReader.TryReadPhsRosterName;
    }

    internal Steam2026InGameMenuSpeechBridge(
        Func<string, NativeMenuSelection?> readConfigValue,
        Func<int, NativeMenuSelection?> readSoundVolume,
        Func<int, PartyMemberSnapshot?> readPartyMember,
        Func<uint, MagicMenuObservationSnapshot?> readMagic,
        Func<StatusMenuSnapshot?> readCurrentStatus,
        TimeSpan settleTime)
        : this(
            readConfigValue,
            readSoundVolume,
            readPartyMember,
            readMagic,
            readCurrentStatus,
            _ => null,
            (_, _) => null,
            _ => null,
            settleTime)
    {
    }

    internal Steam2026InGameMenuSpeechBridge(
        Func<string, NativeMenuSelection?> readConfigValue,
        Func<int, NativeMenuSelection?> readSoundVolume,
        Func<int, PartyMemberSnapshot?> readPartyMember,
        Func<uint, MagicMenuObservationSnapshot?> readMagic,
        Func<StatusMenuSnapshot?> readCurrentStatus,
        TimeSpan settleTime,
        Func<int, StatusMenuSnapshot?> readPartyStatus)
        : this(
            readConfigValue,
            readSoundVolume,
            readPartyMember,
            readMagic,
            readCurrentStatus,
            settleTime)
    {
        this.readPartyStatus = readPartyStatus
            ?? throw new ArgumentNullException(nameof(readPartyStatus));
    }

    internal Steam2026InGameMenuSpeechBridge(
        Func<string, NativeMenuSelection?> readConfigValue,
        Func<int, NativeMenuSelection?> readSoundVolume,
        Func<int, PartyMemberSnapshot?> readPartyMember,
        Func<uint, MagicMenuObservationSnapshot?> readMagic,
        Func<StatusMenuSnapshot?> readCurrentStatus,
        Func<int, InventoryItemSnapshot?> readInventoryItem,
        Func<int, int, NativeMenuSelection?> readEquipment,
        TimeSpan settleTime)
        : this(
            readConfigValue,
            readSoundVolume,
            readPartyMember,
            readMagic,
            readCurrentStatus,
            readInventoryItem,
            readEquipment,
            _ => null,
            settleTime)
    {
    }

    internal Steam2026InGameMenuSpeechBridge(
        Func<string, NativeMenuSelection?> readConfigValue,
        Func<int, NativeMenuSelection?> readSoundVolume,
        Func<int, PartyMemberSnapshot?> readPartyMember,
        Func<uint, MagicMenuObservationSnapshot?> readMagic,
        Func<StatusMenuSnapshot?> readCurrentStatus,
        Func<int, InventoryItemSnapshot?> readInventoryItem,
        Func<int, int, NativeMenuSelection?> readEquipment,
        Func<int, NativeMenuSelection?> readSecondaryEquipment,
        TimeSpan settleTime)
        : this(
            readConfigValue,
            readSoundVolume,
            readPartyMember,
            readMagic,
            readCurrentStatus,
            readInventoryItem,
            readEquipment,
            readSecondaryEquipment,
            () => null,
            settleTime)
    {
    }

    internal Steam2026InGameMenuSpeechBridge(
        Func<string, NativeMenuSelection?> readConfigValue,
        Func<int, NativeMenuSelection?> readSoundVolume,
        Func<int, PartyMemberSnapshot?> readPartyMember,
        Func<uint, MagicMenuObservationSnapshot?> readMagic,
        Func<StatusMenuSnapshot?> readCurrentStatus,
        Func<int, InventoryItemSnapshot?> readInventoryItem,
        Func<int, int, NativeMenuSelection?> readEquipment,
        Func<int, NativeMenuSelection?> readSecondaryEquipment,
        Func<SaveMenuStateSnapshot?> readSaveMenu,
        TimeSpan settleTime)
    {
        this.readConfigValue = readConfigValue ?? throw new ArgumentNullException(nameof(readConfigValue));
        this.readSoundVolume = readSoundVolume ?? throw new ArgumentNullException(nameof(readSoundVolume));
        this.readPartyMember = readPartyMember ?? throw new ArgumentNullException(nameof(readPartyMember));
        readPartyStatus = _ => null;
        this.readMagic = readMagic ?? throw new ArgumentNullException(nameof(readMagic));
        this.readCurrentStatus = readCurrentStatus ?? throw new ArgumentNullException(nameof(readCurrentStatus));
        this.readInventoryItem = readInventoryItem ?? throw new ArgumentNullException(nameof(readInventoryItem));
        readInventorySlot = slot =>
        {
            var item = this.readInventoryItem(slot);
            return item is { } candidate
                ? new InventoryMenuSlotSnapshot(slot, false, candidate)
                : null;
        };
        readAbilitySlot = address =>
        {
            var magic = this.readMagic(address);
            return magic is { } candidate
                ? new AbilityMenuSlotObservationSnapshot(
                    candidate.Widget,
                    new MagicMenuSlotSnapshot(-1, false, candidate.Spell))
                : null;
        };
        this.readEquipment = readEquipment ?? throw new ArgumentNullException(nameof(readEquipment));
        this.readSecondaryEquipment = readSecondaryEquipment ?? throw new ArgumentNullException(nameof(readSecondaryEquipment));
        this.readSaveMenu = readSaveMenu ?? throw new ArgumentNullException(nameof(readSaveMenu));
        this.settleTime = settleTime < TimeSpan.Zero ? TimeSpan.Zero : settleTime;
        ResetTrackers();
    }

    /// <summary>
    /// Polls the checked native Save state after exact active-widget ingress
    /// has acquired the flow. A transient native read failure does not revoke
    /// an acquired Save flow; exact root/module/foreground evidence does.
    /// </summary>
    internal void ObserveSaveMenuState(bool mayOwnMenu, DateTime now)
    {
        if (!mayOwnMenu || now.Kind != DateTimeKind.Utc)
        {
            if (saveMenu.IsActive)
            {
                RevokeOwnership();
            }

            return;
        }

        // The translated x64 Save state uses mode zero, which is also present
        // at the in-game root. State polling therefore cannot acquire this
        // flow; only the exact 5-by-2 Save widget below may do so.
        if (!saveMenu.IsActive)
        {
            return;
        }

        SaveMenuStateSnapshot? state;
        try
        {
            state = readSaveMenu();
        }
        catch
        {
            state = null;
        }

        if (state is not { } snapshot)
        {
            // The native state is deliberately read with bookends. A torn or
            // transitioning page is silence, not loss of an acquired flow.
            return;
        }

        saveMenu.Observe(snapshot, now);
    }

    internal void Observe(
        TranslatedMenuIngressSnapshot snapshot,
        int? moduleId,
        bool isHostForeground,
        bool isNameEntryActive)
    {
        if (!isHostForeground || snapshot.Sequence <= 0 ||
            snapshot.TimestampUtc.Kind != DateTimeKind.Utc)
        {
            RevokeOwnership();
            return;
        }

        var isExactQuitPrompt = IsExactQuitPrompt(snapshot);
        var hasExactQuitEvidence = IsExactQuitEvidenceCurrent(snapshot.TimestampUtc);
        var ownsExactQuitPayload = isExactQuitPrompt ||
            (hasExactQuitEvidence && IsExactQuitRelatedPayload(snapshot));
        var isWorldMapModule = moduleId == WorldMapStateReader.WorldModule;
        var isExactWorldMapMenuWidget = isWorldMapModule &&
            IsExactWorldMapMenuWidgetIngress(snapshot);
        // Module 3 remains active both while traversing the world and while an
        // ordinary FFVII menu is open. Accept its callbacks provisionally so
        // same-frame localized text can precede the widget callback, but Poll
        // stays silent until the exact widget identity below proves ownership.
        var isOwnedNativeModule = IsOwnedNativeModule(moduleId) || isWorldMapModule;
        if ((!isOwnedNativeModule || isNameEntryActive) && !ownsExactQuitPayload)
        {
            // The translated renderer can append unrelated module/name-entry
            // draws after the complete Quit dialog in the same callback batch.
            // Exact prompt evidence is short-lived, so preserve the verified
            // dialog and ignore only those trailing non-Quit payloads.
            if (hasExactQuitEvidence)
            {
                return;
            }

            RevokeOwnership();
            return;
        }

        var routedModuleId = moduleId == PhsModule ? PhsModule : MenuModule;
        if (!ownsMenu || (!ownsExactQuitPayload &&
            (ownedModuleId != routedModuleId || ownsWorldMapIngress != isWorldMapModule)))
        {
            ResetTrackers();
            ownsMenu = true;
            ownsWorldMapIngress = isWorldMapModule;
            ownedModuleId = routedModuleId;
        }

        if (isExactQuitPrompt)
        {
            exactQuitEvidenceExpiresUtc = snapshot.TimestampUtc + ExactQuitEvidenceWindow;
        }

        if (isExactWorldMapMenuWidget)
        {
            exactWorldMapMenuEvidenceExpiresUtc =
                snapshot.TimestampUtc + ExactWorldMapMenuEvidenceWindow;
        }

        if (snapshot.Sequence <= lastSequence)
        {
            RevokeOwnership();
            return;
        }

        lastSequence = snapshot.Sequence;
        try
        {
            if (!TryObserveValidatedPayload(snapshot, routedModuleId))
            {
                RevokeOwnership();
            }
        }
        catch
        {
            RevokeOwnership();
        }
    }

    internal string? Poll(DateTime now)
    {
        if (!ownsMenu || now.Kind != DateTimeKind.Utc)
        {
            return null;
        }

        if (ownsWorldMapIngress && !saveMenu.IsActive &&
            !IsExactQuitEvidenceCurrent(now) &&
            !IsExactWorldMapMenuEvidenceCurrent(now))
        {
            RevokeOwnership();
            return null;
        }

        try
        {
            if (saveMenu.IsActive)
            {
                pendingSaveSpeech = saveMenu.Peek(now);
                return pendingSaveSpeech?.Text;
            }

            // Mirrors the legacy x86 host. PartyFormationSpeechTracker claims the menu module
            // from a title drawn at x=508, y=13 with context 0, and the Magic screen draws its
            // own "Magic" title in exactly that spot, so the Reform/PHS gate below swallowed
            // every spell and spell-target read. The real Reform screen never drives a Magic
            // widget, so an active Magic widget is proof the claim is not ours.
            if (partyFormation.IsActive(now) && activeMenu.LastCompletedWidgetIsMagicScreen)
            {
                partyFormation.Reset();
            }

            if (partyFormation.IsActive(now))
            {
                activeMenu.DiscardPending();
                staticMenu.DiscardPending();
                statusMenu.DiscardPending();
                return partyFormation.Poll(now);
            }

            if (materiaTutorial.IsActive(now))
            {
                activeMenu.DiscardPending();
                return materiaTutorial.Poll(now);
            }

            return activeMenu.Poll()
                   ?? staticMenu.Poll(now, SafeReadConfigValue)
                   ?? statusMenu.Poll(now, SafeReadCurrentStatus);
        }
        catch
        {
            RevokeOwnership();
            return null;
        }
    }

    internal void Reset() => RevokeOwnership();

    internal void AcknowledgeSaveMenuSpeech(string speech)
    {
        if (saveMenu.IsActive && pendingSaveSpeech is { } pending &&
            string.Equals(pending.Text, speech, StringComparison.Ordinal) &&
            saveMenu.Acknowledge(pending.Id))
        {
            pendingSaveSpeech = null;
        }
    }

    internal bool HasSaveMenuOwnership => ownsMenu && saveMenu.IsActive;

    internal bool HasWorldMapMenuOwnership(DateTime now) =>
        ownsMenu && ownsWorldMapIngress && now.Kind == DateTimeKind.Utc &&
        (saveMenu.IsActive || IsExactWorldMapMenuEvidenceCurrent(now));

    internal bool HasExactQuitOwnership(DateTime now) =>
        ownsMenu && now.Kind == DateTimeKind.Utc && IsExactQuitEvidenceCurrent(now);

    internal static bool IsOwnedNativeModule(int? moduleId) =>
        moduleId is MenuModule or PhsModule;

    private static bool IsExactSaveFileWidgetIngress(TranslatedMenuIngressSnapshot snapshot) =>
        snapshot.CallbackKind == Steam2026MenuCallbackKind.ActiveWidgetUpdate
        && snapshot.ActiveWidget is
        {
            WidgetIdentity: SaveMenuStateReader.AddressSaveFileWidget,
            Columns: 5,
            Rows: 2,
            First: >= 0 and < 5,
            Cursor: >= 0 and < 2
        };

    private static bool IsExactWorldMapMenuWidgetIngress(
        TranslatedMenuIngressSnapshot snapshot)
    {
        if (snapshot.CallbackKind != Steam2026MenuCallbackKind.ActiveWidgetUpdate ||
            snapshot.Cursor is not null || snapshot.Text is not null ||
            snapshot.ActiveWidget is not { WidgetIdentity: not 0 } widget ||
            !TryResolveUniqueWidget(widget, out var descriptor))
        {
            return false;
        }

        // Title widgets cannot legitimately own an in-game world-map menu.
        if (descriptor.Kind == MenuWidgetKind.TitleSaveFile ||
            descriptor.Address == 0x00DD6F20)
        {
            return false;
        }

        return descriptor.Address != SaveMenuStateReader.AddressSaveFileWidget ||
            IsExactSaveFileWidgetIngress(snapshot);
    }

    private bool TryObserveValidatedPayload(
        TranslatedMenuIngressSnapshot snapshot,
        int routedModuleId)
    {
        switch (snapshot.CallbackKind)
        {
            case Steam2026MenuCallbackKind.CursorA:
            case Steam2026MenuCallbackKind.CursorB:
                if (snapshot.Cursor is not { } cursor || cursor.Source != snapshot.CallbackKind ||
                    snapshot.ActiveWidget is not null || snapshot.Text is not null)
                {
                    return false;
                }

                if (saveMenu.IsActive)
                {
                    return true;
                }

                var cursorEntry = new MenuCursorDrawObservation(
                    cursor.Source == Steam2026MenuCallbackKind.CursorA ? "A" : "B",
                    routedModuleId,
                    cursor.X,
                    cursor.Y,
                    cursor.Context);
                partyFormation.ObserveCursor(cursorEntry, snapshot.TimestampUtc);
                if (routedModuleId == PhsModule)
                {
                    return true;
                }

                activeMenu.ObserveCursor(cursorEntry);
                staticMenu.ObserveCursor(cursorEntry, snapshot.TimestampUtc);
                return true;

            case Steam2026MenuCallbackKind.EncodedTextA:
            case Steam2026MenuCallbackKind.EncodedTextB:
            case Steam2026MenuCallbackKind.AsciiRenderer:
                if (snapshot.Text is not { } text || text.Source != snapshot.CallbackKind ||
                    snapshot.Cursor is not null || snapshot.ActiveWidget is not null)
                {
                    return false;
                }

                if (saveMenu.IsActive)
                {
                    return true;
                }

                var drawEntry = new MenuTextRenderEntry(
                    text.Text,
                    unchecked((uint)text.X),
                    unchecked((uint)text.Y),
                    text.Color,
                    text.Context);
                partyFormation.ObserveDraw(drawEntry, routedModuleId, snapshot.TimestampUtc);
                if (routedModuleId == PhsModule)
                {
                    return true;
                }

                materiaTutorial.Observe(drawEntry, MenuModule, snapshot.TimestampUtc);
                activeMenu.ObserveDraw(drawEntry);
                staticMenu.ObserveDraw(drawEntry, snapshot.TimestampUtc);
                statusMenu.ObserveDraw(drawEntry, snapshot.TimestampUtc);
                return true;

            case Steam2026MenuCallbackKind.ActiveWidgetUpdate:
                if (snapshot.ActiveWidget is not { } widget || snapshot.Cursor is not null ||
                    snapshot.Text is not null)
                {
                    return false;
                }

                // Normal PHS is driven by its title, character-name draws, and
                // two verified cursor contexts. DAT_00DCA118 is only a PHS
                // selection-state flag, not an ActiveMenuWidget structure.
                if (routedModuleId == PhsModule)
                {
                    return true;
                }

                if (!TryResolveUniqueWidget(widget, out var descriptor))
                {
                    if (!saveMenu.IsActive)
                    {
                        activeMenu.DiscardPending();
                    }

                    return true;
                }

                var enrichedWidget = EnrichWidget(widget, descriptor);
                var wasSaveActive = saveMenu.IsActive;
                saveMenu.ObserveWidget(enrichedWidget);
                if (saveMenu.IsActive)
                {
                    if (!wasSaveActive)
                    {
                        pendingSaveSpeech = null;
                        ResetGenericTrackers();
                    }

                    SaveMenuStateSnapshot? checkedState;
                    try
                    {
                        checkedState = readSaveMenu();
                    }
                    catch
                    {
                        checkedState = null;
                    }

                    if (checkedState is { } saveState)
                    {
                        saveMenu.Observe(saveState, snapshot.TimestampUtc);
                    }

                    return true;
                }

                if (wasSaveActive)
                {
                    pendingSaveSpeech = null;
                    ResetGenericTrackers();
                }

                activeMenu.CompleteFrame(enrichedWidget, snapshot.TimestampUtc);
                return true;

            default:
                return false;
        }
    }

    private ActiveMenuWidgetSnapshot EnrichWidget(
        TranslatedMenuWidgetIngressObservation widget,
        MenuWidgetDescriptor descriptor)
    {
        InventoryItemSnapshot? inventoryItem = null;
        NativeMenuSelection? nativeSelection = null;
        MagicMenuSpellSnapshot? spell = null;
        NativeEmptyMenuSlotSnapshot? emptySlot = null;
        try
        {
            if (widget.Kind == MenuWidgetKind.ItemList &&
                TryGetInventorySlot(widget, out var inventorySlot))
            {
                var slot = readInventorySlot(inventorySlot);
                if (slot is { IsEmpty: true } emptyInventorySlot)
                {
                    emptySlot = new NativeEmptyMenuSlotSnapshot(emptyInventorySlot.Slot);
                }
                else if (slot is { } populatedInventorySlot)
                {
                    inventoryItem = populatedInventorySlot.Item;
                }
            }
            else if (widget.Kind == MenuWidgetKind.ConfigSoundVolume)
            {
                nativeSelection = readSoundVolume(widget.Cursor);
            }
            else if (widget.Kind == MenuWidgetKind.CharacterList)
            {
                nativeSelection = readOrder(descriptor.Address, widget.Cursor);
                if (nativeSelection is null)
                {
                    var member = readPartyMember(widget.Cursor);
                    if (member is { Name.Length: > 0 } partyMember)
                    {
                        nativeSelection = new NativeMenuSelection(
                            partyMember.Name,
                            null,
                            $"party:{descriptor.Address:X8}:{widget.Cursor}:{partyMember.CharacterId}:{partyMember.Name}");
                    }
                }
            }
            else if (widget.Kind is MenuWidgetKind.ItemTarget or MenuWidgetKind.MagicTarget)
            {
                var status = readPartyStatus(widget.Cursor);
                if (status is { Name.Length: > 0 } partyTarget &&
                    partyTarget.PartySlot == widget.Cursor)
                {
                    nativeSelection = PartyTargetMenuSelectionFormatter.Create(
                        partyTarget,
                        descriptor.Address,
                        widget.Cursor);
                }
            }
            else if (widget.Kind == MenuWidgetKind.EquipmentSlot &&
                descriptor.Address == SecondaryEquipmentSlotWidgetIdentity)
            {
                nativeSelection = readSecondaryEquipment(widget.Cursor);
            }
            else if (widget.Kind == MenuWidgetKind.EquipmentList)
            {
                nativeSelection = readEquipmentList();
            }
            else if (widget.Kind is MenuWidgetKind.MateriaSlot or MenuWidgetKind.MateriaList)
            {
                nativeSelection = readMateria(widget.Kind);
            }
            else if (widget.Kind is MenuWidgetKind.MagicList or
                MenuWidgetKind.SummonList or
                MenuWidgetKind.EnemySkillList)
            {
                var observation = readAbilitySlot(descriptor.Address);
                if (observation is { } ability && ability.Widget == ToPublicWidget(widget))
                {
                    if (ability.Slot.IsEmpty)
                    {
                        emptySlot = new NativeEmptyMenuSlotSnapshot(ability.Slot.Slot);
                    }
                    else
                    {
                        spell = ability.Slot.Spell;
                    }
                }
            }
        }
        catch
        {
            // A native enrichment transition is silence for this exact frame.
        }

        return new ActiveMenuWidgetSnapshot(
            descriptor.Address,
            descriptor.Name,
            descriptor.Kind,
            widget.First,
            widget.Cursor,
            widget.Columns,
            widget.Rows,
            widget.ScrollOffset,
            widget.ScrollDelta,
            widget.ScrollState,
            inventoryItem,
            nativeSelection,
            spell,
            emptySlot);
    }

    private static bool TryResolveUniqueWidget(
        TranslatedMenuWidgetIngressObservation widget,
        out MenuWidgetDescriptor descriptor)
    {
        descriptor = default;
        if (string.IsNullOrWhiteSpace(widget.VerifiedName) ||
            widget.Columns is <= 0 or > 16 || widget.Rows is <= 0 or > 400 ||
            widget.First < 0 || widget.First >= widget.Columns ||
            widget.Cursor < 0 || widget.Cursor >= widget.Rows)
        {
            return false;
        }

        if (widget.WidgetIdentity != 0)
        {
            if (!MenuWidgetCatalog.TryResolve(widget.WidgetIdentity, out descriptor) ||
                descriptor.Kind != widget.Kind ||
                !string.Equals(descriptor.Name, widget.VerifiedName, StringComparison.Ordinal))
            {
                descriptor = default;
                return false;
            }

            return true;
        }

        var matches = MenuWidgetCatalog.All
            .Where(candidate => candidate.Kind == widget.Kind &&
                string.Equals(candidate.Name, widget.VerifiedName, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            return false;
        }

        descriptor = matches[0];
        return true;
    }

    private static bool TryGetInventorySlot(
        TranslatedMenuWidgetIngressObservation widget,
        out int slot)
    {
        var candidate = (long)widget.First +
            ((long)widget.Cursor * widget.Columns) +
            ((long)widget.ScrollOffset * widget.Columns);
        if (candidate is < 0 or >= InventoryItemReader.SlotCount)
        {
            slot = default;
            return false;
        }

        slot = (int)candidate;
        return true;
    }

    private static bool IsExactQuitPrompt(TranslatedMenuIngressSnapshot snapshot)
    {
        if (snapshot.CallbackKind is not (
                Steam2026MenuCallbackKind.EncodedTextA or
                Steam2026MenuCallbackKind.EncodedTextB or
                Steam2026MenuCallbackKind.AsciiRenderer) ||
            snapshot.Text is not { } text || text.Source != snapshot.CallbackKind ||
            snapshot.Cursor is not null || snapshot.ActiveWidget is not null ||
            text.Context != QuitPromptContext ||
            text.Text.Trim().Length < 4 ||
            !text.Text.Any(char.IsLetterOrDigit))
        {
            return false;
        }

        return IsHighResolutionQuitPrompt(text.X, text.Y) ||
            IsLowResolutionQuitPrompt(text.X, text.Y);
    }

    private static bool IsExactQuitRelatedPayload(TranslatedMenuIngressSnapshot snapshot)
    {
        if (snapshot.Text is { } text && snapshot.Cursor is null && snapshot.ActiveWidget is null &&
            text.Source == snapshot.CallbackKind && text.Context == QuitPromptContext)
        {
            return (text.X is >= 180 and <= 520 && text.Y is >= 140 and <= 320) ||
                (text.X is >= 90 and <= 260 && text.Y is >= 70 and <= 160);
        }

        if (snapshot.Cursor is { } cursor && snapshot.Text is null && snapshot.ActiveWidget is null &&
            cursor.Source == snapshot.CallbackKind && cursor.Context == RootMainMenuContext)
        {
            return (cursor.X is >= 140 and <= 390 && cursor.Y is >= 290 and <= 315) ||
                (cursor.X is >= 70 and <= 200 && cursor.Y is >= 145 and <= 160);
        }

        return false;
    }

    private static bool IsHighResolutionQuitPrompt(int x, int y) =>
        x is >= 200 and <= 240 && y is >= 140 and <= 175;

    private static bool IsLowResolutionQuitPrompt(int x, int y) =>
        x is >= 100 and <= 120 && y is >= 70 and <= 90;

    private bool IsExactQuitEvidenceCurrent(DateTime now) =>
        exactQuitEvidenceExpiresUtc != DateTime.MinValue &&
        now <= exactQuitEvidenceExpiresUtc;

    private bool IsExactWorldMapMenuEvidenceCurrent(DateTime now) =>
        exactWorldMapMenuEvidenceExpiresUtc != DateTime.MinValue &&
        now <= exactWorldMapMenuEvidenceExpiresUtc;

    private static Steam2026MenuWidgetObservationSnapshot ToPublicWidget(
        TranslatedMenuWidgetIngressObservation widget) =>
        new(
            widget.VerifiedName,
            widget.Kind,
            widget.First,
            widget.Cursor,
            widget.Columns,
            widget.Rows,
            widget.ScrollOffset,
            widget.ScrollDelta,
            widget.ScrollState);

    private NativeMenuSelection? SafeReadConfigValue(string label)
    {
        try { return readConfigValue(label); }
        catch { return null; }
    }

    private StatusMenuSnapshot? SafeReadCurrentStatus()
    {
        try { return readCurrentStatus(); }
        catch { return null; }
    }

    private void RevokeOwnership()
    {
        if (ownsMenu || lastSequence != 0)
        {
            ResetTrackers();
        }

        ownsMenu = false;
        ownsWorldMapIngress = false;
        ownedModuleId = null;
    }

    private void ResetTrackers()
    {
        ResetGenericTrackers();
        saveMenu = new SaveMenuSpeechTracker(settleTime);
        pendingSaveSpeech = null;
        lastSequence = 0;
    }

    private void ResetGenericTrackers()
    {
        activeMenu = new ActiveMenuFrameSpeechCoordinator();
        staticMenu = new StaticMenuCursorSpeechTracker(settleTime);
        statusMenu = new StatusMenuSpeechTracker(settleTime);
        materiaTutorial = new MateriaTutorialSpeechTracker();
        partyFormation = new PartyFormationSpeechTracker(
            settleTime,
            index => readPhsRosterName(index));
        exactQuitEvidenceExpiresUtc = DateTime.MinValue;
        exactWorldMapMenuEvidenceExpiresUtc = DateTime.MinValue;
    }
}
