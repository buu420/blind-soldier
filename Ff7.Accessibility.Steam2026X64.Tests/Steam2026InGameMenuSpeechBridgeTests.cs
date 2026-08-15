using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;
using Ff7.Accessibility.Steam2026X64.Runtime.Menus;

internal static class Steam2026InGameMenuSpeechBridgeTests
{
    private const int MenuModule = 5;
    private const int RootContext = 0x3A83126F;
    private const int ConfigContext = 0x3DCCCCCD;
    private const int ItemArrangeContext = 0x3C23D70A;

    internal static void Run()
    {
        ReadsGenericRenderedSelection();
        ReadsItemCommandWithoutRenderedCursor();
        ReadsItemArrangeWithoutRenderedCursor();
        ReadsLimitLevelConfirmationFromNativeRow();
        ReadsNativeOrderRowsAndPendingSwap();
        ReadsScriptedReformPartySelection();
        ReadsNormalPhsPartySelection();
        ReformValidationDoesNotAlternateWithTranslatedInstruction();
        ReadsMagicCategoryWithoutRenderedCursor();
        ReadsNativeMagicAndPartySelections();
        ReadsNativeItemAndMagicPartyTargets();
        ReadsCheckedInventoryAndExactEquipmentSelections();
        SecondaryEquipmentReaderUsesCheckedSelectorBookends();
        ReadsConfigValueHelpAndStatusSummary();
        ReadsExactQuitChoiceAcrossNameEntryOwnershipCollision();
        ReadsLocalizedQuitChoiceAcrossNameEntryOwnershipCollision();
        RetainsExactQuitChoiceAcrossTrailingUnrelatedDraw();
        ReadsExactLowResolutionQuitChoiceAcrossModuleTransition();
        ReadsMateriaTutorialInstructions();
        SharedMateriaReaderUsesSelectedCharacterRecord();
        ReadsLiveModeZeroSaveOnlyAfterExactWidgetIngress();
        ReadsExactSaveFlowFromWorldMapWithoutOwningGenericWorldCallbacks();
        ReadsVerifiedSubmenuFromWorldMapWithoutOwningGenericWorldCallbacks();
        ReadsNativeSaveFlowAndRetainsTransactionalOwnership();
        ReannouncesOuterSaveAfterFailedInnerRead();
        KeepsOtherOwnersAndAmbiguousWidgetsSilent();
        CurrentStatusReaderUsesCheckedSelectorBookends();
        SharedShopReaderOwnsNativeModuleFive();
    }

    private static void ReadsItemCommandWithoutRenderedCursor()
    {
        var bridge = CreateBridge();
        var now = UtcNow();
        var sequence = 0L;
        ObserveText(bridge, ref sequence, now, "Uzyj", 57, 17, 7, 0x3DCED917);
        ObserveText(bridge, ref sequence, now, "Uloz", 150, 17, 7, 0x3DCED917);
        ObserveText(bridge, ref sequence, now, "Kluczowe przedmioty", 243, 17, 7, 0x3DCED917);
        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Item submenu command",
            MenuWidgetKind.ItemCommand,
            first: 1,
            cursor: 0,
            columns: 3,
            rows: 1,
            widgetIdentity: 0x00DD1A18);

        Equal(
            "Uloz",
            bridge.Poll(now),
            "translated Item command follows the checked native column without a cursor callback");
    }

    private static void ReadsGenericRenderedSelection()
    {
        var bridge = CreateBridge();
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(bridge, ref sequence, now, "Use", 77, 109, 5, ConfigContext);
        ObserveText(bridge, ref sequence, now, "Arrange", 77, 145, 5, ConfigContext);
        ObserveText(bridge, ref sequence, now, "Rearrange items", 16, 13, 7, ConfigContext);
        ObserveCursor(bridge, ref sequence, now, 20, 145, ConfigContext);
        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Item submenu command",
            MenuWidgetKind.ItemCommand,
            first: 0,
            cursor: 1,
            columns: 1,
            rows: 3);

        Equal(
            "Arrange. Rearrange items",
            bridge.Poll(now),
            "generic menu selection uses correlated native text and help");
        Equal(null, bridge.Poll(now), "stable generic menu selection does not repeat");
    }

    private static void ReadsItemArrangeWithoutRenderedCursor()
    {
        Equal(
            true,
            Enum.TryParse<MenuWidgetKind>("ItemArrange", out var arrangeKind),
            "translated Item Arrange kind exists");

        var bridge = CreateBridge();
        var now = UtcNow();
        var sequence = 0L;
        var labels = new[]
        {
            "Customize",
            "Field",
            "Battle",
            "Throw",
            "Type",
            "Name",
            "Most",
            "Least"
        };
        for (var index = 0; index < labels.Length; index++)
        {
            ObserveText(
                bridge,
                ref sequence,
                now,
                labels[index],
                233,
                39 + (26 * index),
                7,
                ItemArrangeContext);
        }

        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Item arrange",
            arrangeKind,
            first: 0,
            cursor: 0,
            columns: 1,
            rows: 8,
            widgetIdentity: 0x00DD1AF8);
        Equal(
            "Customize",
            bridge.Poll(now),
            "translated Item Arrange reads its first native row without a cursor callback");

        for (var index = 0; index < labels.Length; index++)
        {
            ObserveText(
                bridge,
                ref sequence,
                now.AddMilliseconds(16),
                labels[index],
                233,
                39 + (26 * index),
                7,
                ItemArrangeContext);
        }

        ObserveWidget(
            bridge,
            ref sequence,
            now.AddMilliseconds(16),
            "Item arrange",
            arrangeKind,
            first: 0,
            cursor: 6,
            columns: 1,
            rows: 8,
            widgetIdentity: 0x00DD1AF8);
        Equal(
            "Most",
            bridge.Poll(now.AddMilliseconds(16)),
            "translated Item Arrange follows the checked native row without a cursor callback");
    }

    private static void ReadsLimitLevelConfirmationFromNativeRow()
    {
        Equal(
            true,
            Enum.TryParse<MenuWidgetKind>("LimitConfirmation", out var confirmationKind),
            "translated Limit confirmation kind exists");

        var bridge = CreateBridge();
        var now = UtcNow();
        var sequence = 0L;

        AddLimitConfirmationText(
            bridge,
            ref sequence,
            now,
            "To change BREAK LEVEL,",
            "it will begin from Limit Point 0.",
            "Change BREAK LEVEL?",
            "Yes",
            "No");
        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Limit level confirmation",
            confirmationKind,
            first: 0,
            cursor: 1,
            columns: 1,
            rows: 2,
            widgetIdentity: 0x00DCA278);
        Equal(
            "To change BREAK LEVEL, it will begin from Limit Point 0. Change BREAK LEVEL? No",
            bridge.Poll(now),
            "translated Limit confirmation reads the prompt and native default row");

        AddLimitConfirmationText(
            bridge,
            ref sequence,
            now.AddMilliseconds(16),
            "To change BREAK LEVEL,",
            "it will begin from Limit Point 0.",
            "Change BREAK LEVEL?",
            "Yes",
            "No");
        ObserveWidget(
            bridge,
            ref sequence,
            now.AddMilliseconds(16),
            "Limit level confirmation",
            confirmationKind,
            first: 0,
            cursor: 0,
            columns: 1,
            rows: 2,
            widgetIdentity: 0x00DCA278);
        Equal("Yes", bridge.Poll(now.AddMilliseconds(16)), "translated Limit confirmation reads Yes");
    }

    private static void AddLimitConfirmationText(
        Steam2026InGameMenuSpeechBridge bridge,
        ref long sequence,
        DateTime now,
        string warning,
        string consequence,
        string question,
        string yes,
        string no)
    {
        ObserveText(bridge, ref sequence, now, warning, 177, 75, 7, 0);
        ObserveText(bridge, ref sequence, now, consequence, 177, 109, 7, 0);
        ObserveText(bridge, ref sequence, now, question, 177, 143, 7, 0);
        ObserveText(bridge, ref sequence, now, yes, 297, 178, 7, 0);
        ObserveText(bridge, ref sequence, now, no, 297, 203, 7, 0);
    }

    private static void ReadsNativeOrderRowsAndPendingSwap()
    {
        var memory = new Memory();
        var partyBase = SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset;
        var cloudBase = SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset;
        var barretBase = cloudBase + SavemapPartyReader.CharacterSize;
        memory.WriteByte((uint)partyBase, 0);
        memory.WriteByte((uint)(partyBase + 1), 1);
        memory.Write(
            (uint)(cloudBase + SavemapPartyReader.CharacterNameOffset),
            [0x21, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
        memory.Write(
            (uint)(barretBase + SavemapPartyReader.CharacterNameOffset),
            [0x22, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
        memory.WriteByte((uint)(cloudBase + SavemapPartyReader.RowFlagsOffset), 1);
        memory.WriteByte((uint)(barretBase + SavemapPartyReader.RowFlagsOffset), 0);
        memory.WriteInt32(OrderMenuSelectionReader.AddressSelectionLatch, 0);
        memory.WriteByte((uint)OrderMenuSelectionReader.AddressSelectedPartySlot, 0);

        var bridge = new Steam2026InGameMenuSpeechBridge(CreateMenuReader(memory));
        var now = UtcNow();
        var sequence = 0L;

        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Order party",
            MenuWidgetKind.CharacterList,
            first: 0,
            cursor: 0,
            columns: 1,
            rows: 3,
            widgetIdentity: OrderMenuSelectionReader.OrderPartyWidget);
        Equal(
            "A, front row",
            bridge.Poll(now),
            "normal x64 Order exposes the highlighted member's native battle row");

        memory.WriteInt32(OrderMenuSelectionReader.AddressSelectionLatch, 1);
        ObserveWidget(
            bridge,
            ref sequence,
            now.AddMilliseconds(16),
            "Order party",
            MenuWidgetKind.CharacterList,
            first: 0,
            cursor: 1,
            columns: 1,
            rows: 3,
            widgetIdentity: OrderMenuSelectionReader.OrderPartyWidget);
        Equal(
            "B, back row. A selected. Select B to swap",
            bridge.Poll(now.AddMilliseconds(16)),
            "normal x64 Order retains the pending member while the cursor moves");
    }

    private static void ReadsScriptedReformPartySelection()
    {
        var bridge = CreateBridge(settleTime: TimeSpan.FromMilliseconds(30));
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(bridge, ref sequence, now, "Reform", 508, 14, 7, 0);
        ObserveText(
            bridge,
            ref sequence,
            now,
            "Select with START button.",
            26,
            13,
            7,
            ConfigContext);
        ObserveText(bridge, ref sequence, now, "Cloud", 134, 77, 7, ConfigContext);
        ObserveText(bridge, ref sequence, now, "Barret", 134, 214, 7, ConfigContext);
        ObserveText(bridge, ref sequence, now, "Red XIII", 134, 351, 7, ConfigContext);
        ObserveCursor(bridge, ref sequence, now.AddMilliseconds(1), 0, 120, 0x3DCF0D84);
        Equal(
            "Reform. Party slot 1, Cloud. Press Start when finished.",
            bridge.Poll(now.AddMilliseconds(80)),
            "translated Reform active-party cursor");

        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(90),
            "Reform",
            508,
            14,
            7,
            0);
        ObserveCursor(bridge, ref sequence, now.AddMilliseconds(100), 326, 223, 0x3DCD0679);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(105),
            "Tifa",
            438,
            68,
            7,
            0x3DCED917);
        Equal(
            "Available member, Tifa.",
            bridge.Poll(now.AddMilliseconds(180)),
            "translated Reform reserve cursor");

        ObserveCursor(bridge, ref sequence, now.AddMilliseconds(200), 326, 322, 0x3DCD0679);
        Equal(
            "Empty.",
            bridge.Poll(now.AddMilliseconds(280)),
            "translated Reform empty reserve cell");
    }

    private static void ReadsNormalPhsPartySelection()
    {
        const int phsModule = 19;
        var bridge = CreateBridge(settleTime: TimeSpan.FromMilliseconds(30));
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(bridge, ref sequence, now, "PHS", 508, 14, 7, 0, moduleId: phsModule);
        ObserveText(
            bridge,
            ref sequence,
            now,
            "Select with START button.",
            26,
            13,
            7,
            ConfigContext,
            moduleId: phsModule);
        ObserveText(bridge, ref sequence, now, "Cloud", 134, 77, 7, ConfigContext, moduleId: phsModule);
        ObserveText(bridge, ref sequence, now, "Barret", 134, 214, 7, ConfigContext, moduleId: phsModule);
        ObserveText(bridge, ref sequence, now, "Tifa", 134, 351, 7, ConfigContext, moduleId: phsModule);
        ObserveCursor(
            bridge,
            ref sequence,
            now.AddMilliseconds(1),
            0,
            257,
            0x3DCF0D84,
            moduleId: phsModule);

        Equal(
            "PHS. Party slot 2, Barret. Press Start when finished.",
            bridge.Poll(now.AddMilliseconds(80)),
            "translated normal PHS module reads the checked party slot");
    }

    private static void ReformValidationDoesNotAlternateWithTranslatedInstruction()
    {
        var bridge = CreateBridge(settleTime: TimeSpan.FromMilliseconds(30));
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(bridge, ref sequence, now, "Reform", 508, 14, 7, 0);
        ObserveText(
            bridge,
            ref sequence,
            now,
            "Select with Menu button.",
            26,
            13,
            7,
            ConfigContext);
        ObserveText(bridge, ref sequence, now, "Cloud", 134, 77, 7, ConfigContext);
        ObserveText(bridge, ref sequence, now, "Barret", 134, 214, 7, ConfigContext);
        ObserveText(bridge, ref sequence, now, "Red XIII", 134, 351, 7, ConfigContext);
        ObserveCursor(bridge, ref sequence, now.AddMilliseconds(1), 0, 120, 0x3DCF0D84);
        Equal(
            "Reform. Party slot 1, Cloud. Select with Menu button.",
            bridge.Poll(now.AddMilliseconds(80)),
            "translated x64 Reform introduction");

        ObserveText(bridge, ref sequence, now.AddMilliseconds(90), "Reform", 508, 14, 7, 0);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(100),
            "Please make a party of three.",
            26,
            13,
            7,
            ConfigContext);
        Equal(
            "Please make a party of three.",
            bridge.Poll(now.AddMilliseconds(140)),
            "translated x64 Reform validation");

        ObserveText(bridge, ref sequence, now.AddMilliseconds(150), "Reform", 508, 14, 7, 0);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(150),
            "Select with Menu button.",
            26,
            13,
            7,
            ConfigContext);
        Equal(
            null,
            bridge.Poll(now.AddMilliseconds(190)),
            "translated selection instruction does not become repeating status speech");

        ObserveText(bridge, ref sequence, now.AddMilliseconds(200), "Reform", 508, 14, 7, 0);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(200),
            "Please make a party of three.",
            26,
            13,
            7,
            ConfigContext);
        Equal(
            null,
            bridge.Poll(now.AddMilliseconds(240)),
            "same translated validation remains deduplicated across instruction draws");

        ObserveText(bridge, ref sequence, now.AddMilliseconds(245), "Reform", 508, 14, 7, 0);
        ObserveCursor(bridge, ref sequence, now.AddMilliseconds(250), 0, 257, 0x3DCF0D84);
        Equal(
            "Party slot 2, Barret.",
            bridge.Poll(now.AddMilliseconds(330)),
            "translated member speech is not starved by the prompt cycle");
    }

    private static void ReadsNativeItemAndMagicPartyTargets()
    {
        var bridge = CreateBridge(
            partyStatus: slot => slot switch
            {
                0 => TargetStatus(0, 0, "Cloud", 300, 350, 40, 54),
                1 => TargetStatus(1, 1, "Barret", 410, 520, 32, 38),
                _ => null
            });
        var now = UtcNow();
        var sequence = 0L;

        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Item target",
            MenuWidgetKind.ItemTarget,
            first: 0,
            cursor: 0,
            columns: 1,
            rows: 3);
        Equal(
            "Cloud. HP 300 of 350. MP 40 of 54",
            bridge.Poll(now),
            "translated Item target uses native party state");

        ObserveWidget(
            bridge,
            ref sequence,
            now.AddMilliseconds(16),
            "Magic target",
            MenuWidgetKind.MagicTarget,
            first: 0,
            cursor: 1,
            columns: 1,
            rows: 3);
        Equal(
            "Barret. HP 410 of 520. MP 32 of 38",
            bridge.Poll(now.AddMilliseconds(16)),
            "translated Magic target uses native party state");
    }

    private static void SharedMateriaReaderUsesSelectedCharacterRecord()
    {
        var memory = new Memory();
        const uint characterData = 0x01010000;
        var secondRecord =
            characterData + MateriaMenuSelectionReader.MenuCharacterDataSize;

        memory.WriteInt32(
            MateriaMenuSelectionReader.AddressMenuMode,
            MateriaMenuSelectionReader.EquippedSlotMode);
        memory.WriteInt32(
            MateriaMenuSelectionReader.AddressSelectedPartySlot,
            1);
        memory.WriteInt32(
            MateriaMenuSelectionReader.AddressMateriaSlotWidget,
            0);
        memory.WriteInt32(
            MateriaMenuSelectionReader.AddressMateriaSlotWidget + 4,
            0);
        memory.WriteUInt32(
            MateriaMenuSelectionReader.AddressMenuCharacterData,
            characterData);

        memory.WriteByte(
            characterData + SavemapPartyReader.EquippedWeaponOffset,
            0);
        memory.WriteUInt32(characterData + 0x40, 7);
        memory.WriteByte(
            secondRecord + SavemapPartyReader.EquippedWeaponOffset,
            1);
        memory.WriteUInt32(secondRecord + 0x40, 8);
        memory.WriteByte(
            EquipmentStatReader.AddressWeaponMateriaSlots,
            6);
        memory.WriteByte(
            EquipmentStatReader.AddressWeaponMateriaSlots +
            EquipmentStatReader.WeaponRecordSize,
            6);

        var reader = new MateriaMenuSelectionReader(
            memory,
            id => id switch
            {
                7 => "Lightning",
                8 => "Restore",
                _ => null
            });

        Equal(
            true,
            reader.TryRead(MenuWidgetKind.MateriaSlot, out var selection),
            "x64 shared Materia reader resolves selected character");
        Equal(
            "Weapon materia slot 1, Restore",
            selection.Text,
            "x64 selected character Materia");
    }

    private static void SharedShopReaderOwnsNativeModuleFive()
    {
        var memory = new Memory();
        memory.WriteByte((uint)FieldPositionReader.AddressCurrentModule, 5);
        memory.WriteInt32((uint)ShopMenuStateReader.AddressActiveState, 1);
        memory.WriteInt32((uint)ShopMenuStateReader.AddressMenuState, 0);
        memory.WriteInt32((uint)ShopMenuStateReader.AddressTopCommandWidget, 0);
        var reader = CreateMenuReader(memory);

        Equal(
            true,
            reader.TryReadShopMenuOwnership(out var ownsShop) && ownsShop,
            "Steam shared shop reader owns module 5");
        Equal(
            "Buy",
            reader.PollShopMenu(new ShopMenuSpeechTracker()),
            "Steam shared shop command");

        memory.WriteByte((uint)FieldPositionReader.AddressCurrentModule, 19);
        Equal(
            true,
            reader.TryReadShopMenuOwnership(out ownsShop) && !ownsShop,
            "Steam shared shop reader rejects module 19");
    }

    private static void ReadsNativeSaveFlowAndRetainsTransactionalOwnership()
    {
        var now = UtcNow();
        SaveMenuStateSnapshot? state = new(
            SaveMenuPage.SaveFiles,
            1,
            0,
            null,
            0);
        var bridge = CreateSaveBridge(() => state);
        var sequence = 0L;

        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Save file or Quit choice",
            MenuWidgetKind.Generic,
            first: 0,
            cursor: 0,
            columns: 5,
            rows: 2,
            widgetIdentity: SaveMenuStateReader.AddressSaveFileWidget);
        bridge.ObserveSaveMenuState(mayOwnMenu: true, now);
        var file = bridge.Poll(now);
        Equal("Save 1.", file, "checked x64 Save file page");
        bridge.AcknowledgeSaveMenuSpeech(file!);

        ObserveText(bridge, ref sequence, now, "stale generic save text", 40, 40, 7, ConfigContext);
        ObserveCursor(bridge, ref sequence, now, 20, 40, ConfigContext);
        Equal(null, bridge.Poll(now), "generic text stays suppressed while Save owns the menu");

        state = new SaveMenuStateSnapshot(
            SaveMenuPage.Games,
            1,
            2,
            Ff7SaveSlotPreview.Empty,
            0);
        bridge.ObserveSaveMenuState(mayOwnMenu: true, now.AddMilliseconds(10));
        var empty = bridge.Poll(now.AddMilliseconds(10));
        Equal("Game 2. Empty.", empty, "checked x64 empty inner save slot");
        bridge.AcknowledgeSaveMenuSpeech(empty!);

        state = new SaveMenuStateSnapshot(
            SaveMenuPage.Confirmation,
            1,
            2,
            Ff7SaveSlotPreview.Empty,
            0);
        bridge.ObserveSaveMenuState(mayOwnMenu: true, now.AddMilliseconds(20));
        var confirmation = bridge.Poll(now.AddMilliseconds(20));
        Equal("Are you sure? Yes.", confirmation, "checked x64 save confirmation");
        Equal(
            confirmation,
            bridge.Poll(now.AddMilliseconds(21)),
            "unacknowledged x64 save confirmation retries");
        bridge.AcknowledgeSaveMenuSpeech(confirmation!);

        state = new SaveMenuStateSnapshot(
            SaveMenuPage.Confirmation,
            1,
            2,
            Ff7SaveSlotPreview.Empty,
            1);
        bridge.ObserveSaveMenuState(mayOwnMenu: true, now.AddMilliseconds(30));
        var no = bridge.Poll(now.AddMilliseconds(30));
        Equal("No.", no, "checked x64 save confirmation choice");
        bridge.AcknowledgeSaveMenuSpeech(no!);

        state = new SaveMenuStateSnapshot(
            SaveMenuPage.SaveFiles,
            1,
            0,
            null,
            0);
        bridge.ObserveSaveMenuState(mayOwnMenu: true, now.AddMilliseconds(40));
        var reacquired = bridge.Poll(now.AddMilliseconds(40));
        Equal("Save 1.", reacquired, "x64 backing out reacquires the same Save file");
        bridge.AcknowledgeSaveMenuSpeech(reacquired!);

        state = null;
        bridge.ObserveSaveMenuState(mayOwnMenu: true, now.AddMilliseconds(50));
        Equal(true, bridge.HasSaveMenuOwnership, "torn native read retains x64 Save ownership");
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(50),
            "must stay silent",
            40,
            40,
            7,
            ConfigContext);
        Equal(null, bridge.Poll(now.AddMilliseconds(50)), "torn native save page does not leak generic speech");
    }

    private static void ReadsExactSaveFlowFromWorldMapWithoutOwningGenericWorldCallbacks()
    {
        var now = UtcNow();
        SaveMenuStateSnapshot? state = new(
            SaveMenuPage.SaveFiles,
            1,
            0,
            null,
            0);
        var bridge = CreateSaveBridge(() => state);
        var sequence = 0L;

        ObserveText(
            bridge,
            ref sequence,
            now,
            "unrelated world overlay",
            40,
            40,
            7,
            ConfigContext,
            moduleId: WorldMapStateReader.WorldModule);
        Equal(
            null,
            bridge.Poll(now),
            "generic world-map rendering cannot acquire ordinary menu speech");

        ObserveWidget(
            bridge,
            ref sequence,
            now.AddMilliseconds(1),
            "Save file or Quit choice",
            MenuWidgetKind.Generic,
            first: 0,
            cursor: 0,
            columns: 5,
            rows: 2,
            moduleId: WorldMapStateReader.WorldModule,
            widgetIdentity: SaveMenuStateReader.AddressSaveFileWidget);

        Equal(
            true,
            bridge.HasSaveMenuOwnership,
            "the exact 5-by-2 Save widget acquires x64 ownership on the world map");
        bridge.ObserveSaveMenuState(mayOwnMenu: true, now.AddMilliseconds(2));
        Equal(
            "Save 1.",
            bridge.Poll(now.AddMilliseconds(2)),
            "the world-map Save flow reads its checked native slot");
    }

    private static void ReadsVerifiedSubmenuFromWorldMapWithoutOwningGenericWorldCallbacks()
    {
        var bridge = CreateBridge();
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(
            bridge,
            ref sequence,
            now,
            "unrelated world overlay",
            40,
            40,
            7,
            ConfigContext,
            moduleId: WorldMapStateReader.WorldModule);
        Equal(
            null,
            bridge.Poll(now),
            "generic world-map rendering cannot acquire ordinary menu speech");

        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(1),
            "Uzyj",
            57,
            17,
            7,
            0x3DCED917,
            moduleId: WorldMapStateReader.WorldModule);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(1),
            "Uloz",
            150,
            17,
            7,
            0x3DCED917,
            moduleId: WorldMapStateReader.WorldModule);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(1),
            "Kluczowe przedmioty",
            243,
            17,
            7,
            0x3DCED917,
            moduleId: WorldMapStateReader.WorldModule);
        ObserveWidget(
            bridge,
            ref sequence,
            now.AddMilliseconds(1),
            "Item submenu command",
            MenuWidgetKind.ItemCommand,
            first: 1,
            cursor: 0,
            columns: 3,
            rows: 1,
            moduleId: WorldMapStateReader.WorldModule,
            widgetIdentity: 0x00DD1A18);

        Equal(
            "Uloz",
            bridge.Poll(now.AddMilliseconds(1)),
            "an exact cataloged Item widget acquires submenu speech on the world map");
        Equal(
            true,
            bridge.HasWorldMapMenuOwnership(now.AddMilliseconds(1)),
            "the runtime session retains the exact world-map submenu owner");

        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(400),
            "unrelated world overlay",
            40,
            40,
            7,
            ConfigContext,
            moduleId: WorldMapStateReader.WorldModule);
        Equal(
            null,
            bridge.Poll(now.AddMilliseconds(400)),
            "expired exact widget evidence cannot leak unrelated world-map text");
        Equal(
            false,
            bridge.HasWorldMapMenuOwnership(now.AddMilliseconds(400)),
            "expired exact widget evidence releases world-map submenu ownership");
    }

    private static void ReannouncesOuterSaveAfterFailedInnerRead()
    {
        var now = UtcNow();
        SaveMenuStateSnapshot? state = new(
            SaveMenuPage.SaveFiles,
            1,
            0,
            null,
            0);
        var bridge = CreateSaveBridge(() => state);
        var sequence = 0L;

        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Save file or Quit choice",
            MenuWidgetKind.Generic,
            first: 0,
            cursor: 0,
            columns: 5,
            rows: 2,
            widgetIdentity: SaveMenuStateReader.AddressSaveFileWidget);
        bridge.ObserveSaveMenuState(mayOwnMenu: true, now);
        var initial = bridge.Poll(now);
        Equal("Save 1.", initial, "initial outer Save selection");
        bridge.AcknowledgeSaveMenuSpeech(initial!);

        state = null;
        bridge.ObserveSaveMenuState(mayOwnMenu: true, now.AddMilliseconds(1));
        ObserveWidget(
            bridge,
            ref sequence,
            now.AddMilliseconds(2),
            "Save game slot",
            MenuWidgetKind.Generic,
            first: 0,
            cursor: 0,
            columns: 1,
            rows: 3,
            widgetIdentity: SaveMenuStateReader.AddressGameWidget);
        ObserveWidget(
            bridge,
            ref sequence,
            now.AddMilliseconds(3),
            "Save file or Quit choice",
            MenuWidgetKind.Generic,
            first: 0,
            cursor: 0,
            columns: 5,
            rows: 2,
            widgetIdentity: SaveMenuStateReader.AddressSaveFileWidget);

        state = new SaveMenuStateSnapshot(SaveMenuPage.SaveFiles, 1, 0, null, 0);
        bridge.ObserveSaveMenuState(mayOwnMenu: true, now.AddMilliseconds(4));
        Equal(
            "Save 1.",
            bridge.Poll(now.AddMilliseconds(4)),
            "returning from a transiently unreadable game list reannounces the same outer slot");
    }

    private static void ReadsLiveModeZeroSaveOnlyAfterExactWidgetIngress()
    {
        var memory = new Memory();
        memory.WriteInt32(SaveMenuStateReader.AddressMode, 0);
        memory.WriteInt32(SaveMenuStateReader.AddressPage, (int)SaveMenuPage.SaveFiles);
        memory.WriteInt32(SaveMenuStateReader.AddressSaveFileWidget + 0x00, 0);
        memory.WriteInt32(SaveMenuStateReader.AddressSaveFileWidget + 0x04, 0);
        var menuReader = CreateMenuReader(memory);

        Equal(
            false,
            new SaveMenuStateReader(memory).TryRead(out _),
            "shared Save reader still rejects mode zero without active-widget ownership");
        Equal(
            true,
            menuReader.TryReadSaveMenu(out var liveState),
            "x64 active-widget Save reader accepts the observed live mode-zero state");
        Equal(SaveMenuPage.SaveFiles, liveState.Page, "mode-zero Save page");
        Equal(1, liveState.SaveFileNumber, "mode-zero Save file selection");

        var bridge = CreateSaveBridge(
            () => menuReader.TryReadSaveMenu(out var snapshot) ? snapshot : null);
        var now = UtcNow();
        var sequence = 0L;

        bridge.ObserveSaveMenuState(mayOwnMenu: true, now);
        Equal(
            false,
            bridge.HasSaveMenuOwnership,
            "mode-zero state alone cannot steal the in-game root menu");
        Equal(null, bridge.Poll(now), "root menu receives no stale Save speech");

        ObserveWidget(
            bridge,
            ref sequence,
            now.AddMilliseconds(1),
            "Save file or Quit choice",
            MenuWidgetKind.Generic,
            first: 0,
            cursor: 0,
            columns: 1,
            rows: 2,
            widgetIdentity: SaveMenuStateReader.AddressSaveFileWidget);
        Equal(
            false,
            bridge.HasSaveMenuOwnership,
            "same-address non-Save geometry cannot acquire Save ownership");

        ObserveWidget(
            bridge,
            ref sequence,
            now.AddMilliseconds(2),
            "Save file or Quit choice",
            MenuWidgetKind.Generic,
            first: 0,
            cursor: 0,
            columns: 5,
            rows: 2,
            widgetIdentity: SaveMenuStateReader.AddressSaveFileWidget);
        Equal(true, bridge.HasSaveMenuOwnership, "exact 5-by-2 Save widget acquires ownership");
        Equal(
            "Save 1.",
            bridge.Poll(now.AddMilliseconds(2)),
            "exact Save widget immediately reads the checked native selection");
    }

    private static void ReadsNativeMagicAndPartySelections()
    {
        var now = UtcNow();
        var magicWidget = new Steam2026MenuWidgetObservationSnapshot(
            "Magic list",
            MenuWidgetKind.MagicList,
            First: 0,
            Cursor: 1,
            Columns: 1,
            Rows: 7,
            ScrollOffset: 0,
            ScrollDelta: 0,
            ScrollState: 0);
        var magic = new MagicMenuObservationSnapshot(
            magicWidget,
            new MagicMenuSpellSnapshot(
                SpellId: 7,
                MpCost: 4,
                Name: "Fire",
                Description: "Fire damage"));
        var bridge = CreateBridge(
            party: slot => slot == 1 ? new PartyMemberSnapshot(0, "Cloud") : null,
            magic: address => address == 0x00DD1708 ? magic : null);
        var sequence = 0L;

        ObserveWidget(bridge, ref sequence, now, magicWidget);
        Equal(
            "Fire. 4 MP. Fire damage",
            bridge.Poll(now),
            "Magic list uses checked native spell details");

        ObserveWidget(
            bridge,
            ref sequence,
            now.AddMilliseconds(50),
            "Main menu party",
            MenuWidgetKind.CharacterList,
            first: 0,
            cursor: 1,
            columns: 1,
            rows: 3);
        Equal("Cloud", bridge.Poll(now.AddMilliseconds(50)), "party list uses checked native party member");
    }

    private static void ReadsMagicCategoryWithoutRenderedCursor()
    {
        var bridge = CreateBridge();
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(bridge, ref sequence, now, "Magia", 508, 56, 7, 0x3DCED917);
        ObserveText(bridge, ref sequence, now, "Przywołanie", 508, 90, 7, 0x3DCED917);
        ObserveText(bridge, ref sequence, now, "Umiejętność wroga", 508, 124, 7, 0x3DCED917);
        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Magic category",
            MenuWidgetKind.MagicCategory,
            first: 0,
            cursor: 2,
            columns: 1,
            rows: 3,
            widgetIdentity: 0x00DD1698);

        Equal(
            "Umiejętność wroga",
            bridge.Poll(now),
            "translated Magic category uses its localized native row without a cursor callback");
    }

    private static void ReadsCheckedInventoryAndExactEquipmentSelections()
    {
        var memory = CreateInventoryAndEquipmentMemory();
        var reader = CreateMenuReaderWithItems(memory);
        Equal(true, reader.TryReadPartyMember(0, out var equippedMember), "Equip party member is checked and readable");
        Equal("AA", equippedMember.Name, "Equip party member keeps the native name");
        var bridge = new Steam2026InGameMenuSpeechBridge(reader);
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(bridge, ref sequence, now, "Ether", 373, 146, 7, 0x3DCED917);
        ObserveCursor(bridge, ref sequence, now, 298, 146, ConfigContext);
        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Item list",
            MenuWidgetKind.ItemList,
            first: 0,
            cursor: 1,
            columns: 1,
            rows: 10,
            widgetIdentity: 0x00DD1A50);
        Equal(
            "Ether x2. Restores MP by 100",
            bridge.Poll(now),
            "Item list uses the checked native savemap item and kernel text");

        now = now.AddMilliseconds(25);
        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Item arrange list",
            MenuWidgetKind.ItemList,
            first: 0,
            cursor: 0,
            columns: 1,
            rows: 10,
            scrollOffset: 1,
            widgetIdentity: 0x00DD1B30);
        Equal(
            "Ether x2. Restores MP by 100",
            bridge.Poll(now),
            "Customize/manual sort resolves its checked inventory slot");

        now = now.AddMilliseconds(25);
        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Item list",
            MenuWidgetKind.ItemList,
            first: 0,
            cursor: 2,
            columns: 1,
            rows: 10,
            widgetIdentity: 0x00DD1A50);
        Equal(
            "Buster Sword x1",
            bridge.Poll(now),
            "Items menu resolves composite weapon inventory identifiers");

        now = now.AddMilliseconds(50);
        ObserveText(bridge, ref sequence, now, "Check", 57, 17, 7, 0x3DCED917);
        ObserveText(bridge, ref sequence, now, "Arrange", 57, 45, 7, 0x3DCED917);
        ObserveCursor(bridge, ref sequence, now, 13, 17, ConfigContext);
        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Materia command",
            MenuWidgetKind.MateriaCommand,
            first: 0,
            cursor: 0,
            columns: 1,
            rows: 2,
            widgetIdentity: 0x00DD12B8);
        Equal("Check", bridge.Poll(now), "Materia command selector reads its rendered native command");

        now = now.AddMilliseconds(50);
        ObserveText(bridge, ref sequence, now, "Equips Restore magic", 16, 159, 7, 0x3E4CCCCD);
        ObserveText(bridge, ref sequence, now, "Restore", 40, 214, 7, 0x3DCED917);
        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Materia slot",
            MenuWidgetKind.MateriaSlot,
            first: 0,
            cursor: 0,
            columns: 8,
            rows: 2,
            widgetIdentity: 0x00DD12F0);
        Equal(
            "Weapon materia slot 1, Restore. Equips Restore magic",
            bridge.Poll(now),
            "Materia sockets remain independent from the Equip party selector");

        now = now.AddMilliseconds(50);
        ObserveWidget(
            bridge,
            ref sequence,
            now,
            "Equip slot",
            MenuWidgetKind.EquipmentSlot,
            first: 0,
            cursor: 0,
            columns: 1,
            rows: 3,
            widgetIdentity: 0x00DCA5C0);
        Equal(
            "Weapon, Weapon 2. Attack 20. Attack percentage 95 percent. " +
            "Materia slots 2, one linked pair. Growth Normal",
            bridge.Poll(now),
            "secondary Equip identity uses its own checked native party selector");
    }

    private static void ReadsConfigValueHelpAndStatusSummary()
    {
        var status = CreateStatus();
        var bridge = CreateBridge(
            config: label => string.Equals(label, "Battle message", StringComparison.Ordinal)
                ? new NativeMenuSelection(
                    "50 percent from Fast to Slow",
                    null,
                    "config:Battle message:128")
                : null,
            status: () => status,
            settleTime: TimeSpan.FromMilliseconds(30));
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(bridge, ref sequence, now, "Config", 508, 13, 7, RootContext);
        ObserveText(bridge, ref sequence, now, "Set battle message speed", 16, 13, 7, ConfigContext);
        ObserveCursor(bridge, ref sequence, now.AddMilliseconds(2), 6, 313, ConfigContext);
        ObserveText(bridge, ref sequence, now.AddMilliseconds(4), "Battle message", 62, 307, 5, ConfigContext);
        ObserveText(bridge, ref sequence, now.AddMilliseconds(4), "Fast", 264, 307, 7, ConfigContext);
        ObserveText(bridge, ref sequence, now.AddMilliseconds(4), "Slow", 526, 307, 7, ConfigContext);

        Equal(null, bridge.Poll(now.AddMilliseconds(20)), "Config waits for the native frame to settle");
        Equal(
            "Battle message. 50 percent from Fast to Slow. Set battle message speed",
            bridge.Poll(now.AddMilliseconds(40)),
            "Config uses the checked native value and rendered help");

        now = now.AddSeconds(1);
        ObserveText(bridge, ref sequence, now, "Status", 508, 13, 7, RootContext);
        ObserveText(bridge, ref sequence, now.AddMilliseconds(4), "Strength", 60, 120, 5, ConfigContext);
        var statusSpeech = bridge.Poll(now.AddMilliseconds(40));
        Contains(statusSpeech, "Cloud. Level 7. HP 314 of 314. MP 54 of 54", "Status identity and resources");
        Contains(statusSpeech, "Weapon Buster Sword. Armor Bronze Bangle. Accessory None", "Status equipment details");
    }

    private static void SecondaryEquipmentReaderUsesCheckedSelectorBookends()
    {
        var stable = CreateInventoryAndEquipmentMemory();
        var reader = CreateMenuReaderWithItems(stable);
        Equal(
            true,
            reader.TryReadSecondaryEquipment(0, out var equipment),
            "secondary Equipment selector is readable");
        Equal("Weapon, Weapon 2", equipment.Text, "secondary Equipment selector chooses party slot one");

        var changed = CreateInventoryAndEquipmentMemory();
        changed.WriteInt32(Steam2026MenuObservationReader.SecondaryEquipmentPartySlotAddress, 0);
        var tearing = new TearingMemory(
            stable,
            changed,
            Steam2026MenuObservationReader.SecondaryEquipmentPartySlotAddress);
        reader = CreateMenuReaderWithItems(tearing);
        Equal(
            false,
            reader.TryReadSecondaryEquipment(0, out equipment),
            "torn secondary Equipment selector fails closed");
        Equal(default(NativeMenuSelection), equipment, "torn secondary Equipment output remains empty");
    }

    private static void ReadsExactQuitChoiceAcrossNameEntryOwnershipCollision()
    {
        var bridge = CreateBridge(settleTime: TimeSpan.Zero);
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(
            bridge,
            ref sequence,
            now,
            "Do you want to quit",
            220,
            158,
            7,
            0x3C23D70A,
            isNameEntryActive: true);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(1),
            "Yes",
            212,
            296,
            0,
            0x3C23D70A,
            isNameEntryActive: true);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(2),
            "No",
            414,
            296,
            7,
            0x3C23D70A,
            isNameEntryActive: true);
        ObserveCursor(
            bridge,
            ref sequence,
            now.AddMilliseconds(3),
            364,
            304,
            RootContext,
            isNameEntryActive: true);

        Equal(
            "No",
            bridge.Poll(now.AddMilliseconds(3)),
            "exact native Quit evidence outranks the overlapping name-entry module state");
        Equal(
            true,
            bridge.HasExactQuitOwnership(now.AddMilliseconds(3)),
            "the session poll gate retains exact Quit ownership");
    }

    private static void ReadsLocalizedQuitChoiceAcrossNameEntryOwnershipCollision()
    {
        var bridge = CreateBridge(settleTime: TimeSpan.Zero);
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(
            bridge,
            ref sequence,
            now,
            "ゲームを終了しますか？",
            220,
            158,
            7,
            0x3C23D70A,
            isNameEntryActive: true);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(1),
            "はい",
            212,
            296,
            0,
            0x3C23D70A,
            isNameEntryActive: true);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(2),
            "いいえ",
            414,
            296,
            7,
            0x3C23D70A,
            isNameEntryActive: true);
        ObserveCursor(
            bridge,
            ref sequence,
            now.AddMilliseconds(3),
            364,
            304,
            RootContext,
            isNameEntryActive: true);

        Equal("いいえ", bridge.Poll(now.AddMilliseconds(3)), "localized native Quit choice");
        Equal(true, bridge.HasExactQuitOwnership(now.AddMilliseconds(3)), "localized Quit ownership");
    }

    private static void RetainsExactQuitChoiceAcrossTrailingUnrelatedDraw()
    {
        var bridge = CreateBridge(settleTime: TimeSpan.Zero);
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(
            bridge,
            ref sequence,
            now,
            "Do you want to quit",
            220,
            158,
            7,
            0x3C23D70A,
            moduleId: 20,
            isNameEntryActive: true);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(1),
            "Yes",
            212,
            296,
            0,
            0x3C23D70A,
            moduleId: 20,
            isNameEntryActive: true);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(2),
            "No",
            414,
            296,
            7,
            0x3C23D70A,
            moduleId: 20,
            isNameEntryActive: true);
        ObserveCursor(
            bridge,
            ref sequence,
            now.AddMilliseconds(3),
            364,
            304,
            RootContext,
            moduleId: 20,
            isNameEntryActive: true);

        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(4),
            "Cloud",
            20,
            20,
            7,
            ConfigContext,
            moduleId: 20,
            isNameEntryActive: true);

        Equal(
            "No",
            bridge.Poll(now.AddMilliseconds(4)),
            "a trailing unrelated renderer draw must not erase an exact Quit choice from the same frame batch");
        Equal(
            true,
            bridge.HasExactQuitOwnership(now.AddMilliseconds(4)),
            "trailing unrelated renderer data must not revoke current exact Quit ownership");
    }

    private static void ReadsExactLowResolutionQuitChoiceAcrossModuleTransition()
    {
        var bridge = CreateBridge(settleTime: TimeSpan.Zero);
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(
            bridge,
            ref sequence,
            now,
            "Do you want to quit",
            110,
            79,
            7,
            0x3C23D70A,
            moduleId: 20,
            isNameEntryActive: true);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(1),
            "Yes",
            106,
            148,
            0,
            0x3C23D70A,
            moduleId: 20,
            isNameEntryActive: true);
        ObserveText(
            bridge,
            ref sequence,
            now.AddMilliseconds(2),
            "No",
            207,
            148,
            7,
            0x3C23D70A,
            moduleId: 20,
            isNameEntryActive: true);
        ObserveCursor(
            bridge,
            ref sequence,
            now.AddMilliseconds(3),
            182,
            152,
            RootContext,
            moduleId: 20,
            isNameEntryActive: true);

        Equal(
            "No",
            bridge.Poll(now.AddMilliseconds(3)),
            "the verified low-resolution Quit layout retains exact ownership across a module transition");
    }

    private static void ReadsMateriaTutorialInstructions()
    {
        var bridge = CreateBridge();
        var now = UtcNow();
        var sequence = 0L;

        ObserveText(bridge, ref sequence, now, "Select a Materia slot.", 60, 100, 7, 0);
        ObserveText(bridge, ref sequence, now.AddMilliseconds(1), "Tutorial", 60, 420, 7, 0);

        Equal(
            "Select a Materia slot.",
            bridge.Poll(now.AddMilliseconds(2)),
            "Materia tutorial uses its verified rendered sentinel");
    }

    private static void KeepsOtherOwnersAndAmbiguousWidgetsSilent()
    {
        var now = UtcNow();

        var root = CreateBridge();
        var sequence = 0L;
        AddRenderedSelection(root, ref sequence, now);
        ObserveWidget(
            root,
            ref sequence,
            now,
            "Item/Main list",
            MenuWidgetKind.RootMainMenu,
            first: 0,
            cursor: 1,
            columns: 1,
            rows: 11);
        Equal(null, root.Poll(now), "root menu remains owned by the existing main-menu reader");

        var ambiguous = CreateBridge(
            party: slot => new PartyMemberSnapshot(slot, slot == 1 ? "Barret" : "Cloud"));
        sequence = 0;
        AddRenderedSelection(ambiguous, ref sequence, now);
        ObserveWidget(
            ambiguous,
            ref sequence,
            now,
            "Order party",
            MenuWidgetKind.CharacterList,
            first: 0,
            cursor: 1,
            columns: 1,
            rows: 3);
        Equal(null, ambiguous.Poll(now), "address-free duplicate widget identity fails closed");

        var nameEntry = CreateBridge();
        sequence = 0;
        AddRenderedSelection(nameEntry, ref sequence, now);
        ObserveWidget(
            nameEntry,
            ref sequence,
            now,
            "Item submenu command",
            MenuWidgetKind.ItemCommand,
            first: 0,
            cursor: 1,
            columns: 1,
            rows: 3);
        ObserveText(
            nameEntry,
            ref sequence,
            now.AddMilliseconds(1),
            "Please enter a name.",
            53,
            30,
            7,
            ConfigContext,
            isNameEntryActive: true);
        Equal(null, nameEntry.Poll(now.AddMilliseconds(1)), "name entry revokes deeper-menu ownership");

        foreach (var module in new[] { 1, 20 })
        {
            var foreign = CreateBridge();
            sequence = 0;
            AddRenderedSelection(foreign, ref sequence, now, moduleId: module);
            ObserveWidget(
                foreign,
                ref sequence,
                now,
                "Item submenu command",
                MenuWidgetKind.ItemCommand,
                first: 0,
                cursor: 1,
                columns: 1,
                rows: 3,
                moduleId: module);
            Equal(null, foreign.Poll(now), $"module {module} remains owned by another speech path");
        }

        var background = CreateBridge();
        sequence = 0;
        AddRenderedSelection(background, ref sequence, now, isHostForeground: false);
        ObserveWidget(
            background,
            ref sequence,
            now,
            "Item submenu command",
            MenuWidgetKind.ItemCommand,
            first: 0,
            cursor: 1,
            columns: 1,
            rows: 3,
            isHostForeground: false);
        Equal(null, background.Poll(now), "background game cannot produce menu speech");
    }

    private static void CurrentStatusReaderUsesCheckedSelectorBookends()
    {
        var stable = CreateStatusMemory(statusPartySlot: 0);
        var reader = CreateMenuReader(stable);

        Equal(true, reader.TryReadCurrentStatusSummary(out var status), "current Status selector is readable");
        Equal(0, status.PartySlot, "current Status selector chooses party slot zero");
        Equal("A", status.Name, "current Status selector returns the checked native character");

        var changed = CreateStatusMemory(statusPartySlot: 1);
        var tearing = new TearingMemory(
            stable,
            changed,
            Steam2026MenuObservationReader.CurrentStatusPartySlotAddress);
        reader = CreateMenuReader(tearing);

        Equal(false, reader.TryReadCurrentStatusSummary(out status), "torn current Status selector fails closed");
        Equal(default(StatusMenuSnapshot), status, "torn current Status output remains empty");
    }

    private static Steam2026InGameMenuSpeechBridge CreateBridge(
        Func<string, NativeMenuSelection?>? config = null,
        Func<int, NativeMenuSelection?>? sound = null,
        Func<int, PartyMemberSnapshot?>? party = null,
        Func<uint, MagicMenuObservationSnapshot?>? magic = null,
        Func<StatusMenuSnapshot?>? status = null,
        Func<int, StatusMenuSnapshot?>? partyStatus = null,
        TimeSpan? settleTime = null) =>
        new(
            config ?? (_ => null),
            sound ?? (_ => null),
            party ?? (_ => null),
            magic ?? (_ => null),
            status ?? (() => null),
            settleTime ?? TimeSpan.Zero,
            partyStatus ?? (_ => null));

    private static StatusMenuSnapshot TargetStatus(
        int partySlot,
        int characterId,
        string name,
        int currentHp,
        int maxHp,
        int currentMp,
        int maxMp) =>
        new(
            partySlot,
            characterId,
            name,
            1,
            currentHp,
            maxHp,
            currentMp,
            maxMp,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            1,
            null,
            null,
            null);

    private static Steam2026InGameMenuSpeechBridge CreateSaveBridge(
        Func<SaveMenuStateSnapshot?> readSaveMenu) =>
        new(
            _ => null,
            _ => null,
            _ => null,
            _ => null,
            () => null,
            _ => null,
            (_, _) => null,
            _ => null,
            readSaveMenu,
            TimeSpan.Zero);

    private static void AddRenderedSelection(
        Steam2026InGameMenuSpeechBridge bridge,
        ref long sequence,
        DateTime now,
        int moduleId = MenuModule,
        bool isHostForeground = true)
    {
        ObserveText(
            bridge,
            ref sequence,
            now,
            "Arrange",
            77,
            145,
            5,
            ConfigContext,
            moduleId,
            isHostForeground);
        ObserveCursor(
            bridge,
            ref sequence,
            now,
            20,
            145,
            ConfigContext,
            moduleId,
            isHostForeground);
    }

    private static void ObserveText(
        Steam2026InGameMenuSpeechBridge bridge,
        ref long sequence,
        DateTime now,
        string text,
        int x,
        int y,
        int color,
        int context,
        int moduleId = MenuModule,
        bool isHostForeground = true,
        bool isNameEntryActive = false)
    {
        bridge.Observe(
            new TranslatedMenuIngressSnapshot(
                Steam2026MenuCallbackKind.EncodedTextB,
                ++sequence,
                now,
                null,
                null,
                new TranslatedMenuTextObservation(
                    Steam2026MenuCallbackKind.EncodedTextB,
                    text,
                    x,
                    y,
                    color,
                    context)),
            moduleId,
            isHostForeground,
            isNameEntryActive);
    }

    private static void ObserveCursor(
        Steam2026InGameMenuSpeechBridge bridge,
        ref long sequence,
        DateTime now,
        int x,
        int y,
        int context,
        int moduleId = MenuModule,
        bool isHostForeground = true,
        bool isNameEntryActive = false)
    {
        bridge.Observe(
            new TranslatedMenuIngressSnapshot(
                Steam2026MenuCallbackKind.CursorB,
                ++sequence,
                now,
                new TranslatedMenuCursorObservation(
                    Steam2026MenuCallbackKind.CursorB,
                    x,
                    y,
                    context),
                null,
                null),
            moduleId,
            isHostForeground,
            isNameEntryActive);
    }

    private static void ObserveWidget(
        Steam2026InGameMenuSpeechBridge bridge,
        ref long sequence,
        DateTime now,
        Steam2026MenuWidgetObservationSnapshot widget,
        int moduleId = MenuModule,
        bool isHostForeground = true,
        bool isNameEntryActive = false) =>
        ObserveWidget(
            bridge,
            ref sequence,
            now,
            widget.VerifiedName,
            widget.Kind,
            widget.First,
            widget.Cursor,
            widget.Columns,
            widget.Rows,
            widget.ScrollOffset,
            widget.ScrollDelta,
            widget.ScrollState,
            moduleId,
            isHostForeground,
            isNameEntryActive);

    private static void ObserveWidget(
        Steam2026InGameMenuSpeechBridge bridge,
        ref long sequence,
        DateTime now,
        string name,
        MenuWidgetKind kind,
        int first,
        int cursor,
        int columns,
        int rows,
        int scrollOffset = 0,
        int scrollDelta = 0,
        int scrollState = 0,
        int moduleId = MenuModule,
        bool isHostForeground = true,
        bool isNameEntryActive = false,
        uint widgetIdentity = 0)
    {
        var widget = new TranslatedMenuWidgetIngressObservation(
            name,
            kind,
            first,
            cursor,
            columns,
            rows,
            scrollOffset,
            scrollDelta,
            scrollState)
        {
            WidgetIdentity = widgetIdentity
        };

        bridge.Observe(
            new TranslatedMenuIngressSnapshot(
                Steam2026MenuCallbackKind.ActiveWidgetUpdate,
                ++sequence,
                now,
                null,
                widget,
                null),
            moduleId,
            isHostForeground,
            isNameEntryActive);
    }

    private static Steam2026MenuObservationReader CreateMenuReader(ILegacyAddressSpace memory) =>
        new(
            memory,
            _ => null,
            _ => null,
            id => $"Weapon {id}",
            id => $"Armor {id}",
            id => $"Accessory {id}");

    private static Steam2026MenuObservationReader CreateMenuReaderWithItems(
        ILegacyAddressSpace memory)
    {
        var constructor = typeof(Steam2026MenuObservationReader)
            .GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .SingleOrDefault(candidate => candidate.GetParameters()
                .Any(parameter => string.Equals(
                    parameter.Name,
                    "resolveItemName",
                    StringComparison.Ordinal)));
        if (constructor is null)
        {
            throw new InvalidOperationException(
                "Steam 2026 menu observations do not expose checked inventory text resolution.");
        }

        return (Steam2026MenuObservationReader)constructor.Invoke(
        [
            memory,
            (Func<int, string?>)(_ => null),
            (Func<int, string?>)(_ => null),
            (Func<int, string?>)(id => id == 1 ? "Buster Sword" : $"Weapon {id}"),
            (Func<int, string?>)(id => $"Armor {id}"),
            (Func<int, string?>)(id => $"Accessory {id}"),
            (Func<int, string?>)(id => id switch
            {
                3 => "Ether",
                128 => "Buster Sword",
                _ => $"Item {id}"
            }),
            (Func<int, string?>)(id => id == 3 ? "Restores MP by 100" : null),
            SavemapPartyReader.AddressSavemap
        ]);
    }

    private static Memory CreateInventoryAndEquipmentMemory()
    {
        var memory = new Memory();
        memory.WriteUInt16(
            (uint)(InventoryItemReader.AddressSavemap + InventoryItemReader.ItemsOffset),
            (ushort)((1 << 9) | 0));
        memory.WriteUInt16(
            (uint)(InventoryItemReader.AddressSavemap + InventoryItemReader.ItemsOffset + sizeof(ushort)),
            (ushort)((2 << 9) | 3));
        memory.WriteUInt16(
            (uint)(InventoryItemReader.AddressSavemap + InventoryItemReader.ItemsOffset + (2 * sizeof(ushort))),
            (ushort)((1 << 9) | 128));

        memory.WriteByte(
            (uint)(SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset),
            0);
        var characterBase = SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset;
        memory.Write(
            (uint)(characterBase + SavemapPartyReader.CharacterNameOffset),
            [0x21, 0x21, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
        memory.WriteByte((uint)(characterBase + SavemapPartyReader.EquippedWeaponOffset), 1);
        memory.WriteByte((uint)(characterBase + SavemapPartyReader.EquippedArmorOffset), 2);
        memory.WriteByte((uint)(characterBase + SavemapPartyReader.EquippedAccessoryOffset), 0xFF);

        memory.WriteByte(
            (uint)(SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset + 1),
            1);
        var secondCharacterBase = characterBase + SavemapPartyReader.CharacterSize;
        memory.Write(
            (uint)(secondCharacterBase + SavemapPartyReader.CharacterNameOffset),
            [0x22, 0x22, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
        memory.WriteByte((uint)(secondCharacterBase + SavemapPartyReader.EquippedWeaponOffset), 2);
        memory.WriteByte((uint)(secondCharacterBase + SavemapPartyReader.EquippedArmorOffset), 3);
        memory.WriteByte((uint)(secondCharacterBase + SavemapPartyReader.EquippedAccessoryOffset), 0xFF);
        memory.WriteByte(
            (uint)(EquipmentStatReader.AddressWeaponAttack +
                (2 * EquipmentStatReader.WeaponRecordSize)),
            20);
        memory.WriteByte(
            (uint)(EquipmentStatReader.AddressWeaponAttackPercent +
                (2 * EquipmentStatReader.WeaponRecordSize)),
            95);
        memory.Write(
            (uint)(EquipmentStatReader.AddressWeaponMateriaSlots +
                (2 * EquipmentStatReader.WeaponRecordSize)),
            [6, 7, 0, 0, 0, 0, 0, 0]);
        memory.WriteByte(
            (uint)(EquipmentStatReader.AddressWeaponGrowth +
                (2 * EquipmentStatReader.WeaponRecordSize)),
            1);
        memory.WriteInt32(0x00DCA4A4, 1);
        return memory;
    }

    private static Memory CreateStatusMemory(int statusPartySlot)
    {
        var memory = new Memory();
        memory.WriteInt32(Steam2026MenuObservationReader.CurrentStatusPartySlotAddress, statusPartySlot);
        memory.WriteByte((uint)(SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset), 0);

        var characterBase = SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset;
        memory.Write(
            (uint)(characterBase + SavemapPartyReader.CharacterNameOffset),
            [0x21, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);
        memory.WriteByte((uint)(characterBase + SavemapPartyReader.LevelOffset), 15);
        memory.WriteByte((uint)(characterBase + SavemapPartyReader.LimitLevelOffset), 2);
        memory.Write(
            (uint)(characterBase + SavemapPartyReader.EquippedWeaponOffset),
            [1, 2, 0xFF]);
        memory.WriteUInt16((uint)(characterBase + SavemapPartyReader.CurrentHpOffset), 300);
        memory.WriteUInt16((uint)(characterBase + SavemapPartyReader.CurrentMpOffset), 40);
        memory.WriteUInt16((uint)(characterBase + SavemapPartyReader.MaxHpOffset), 500);
        memory.WriteUInt16((uint)(characterBase + SavemapPartyReader.MaxMpOffset), 60);
        memory.WriteUInt32((uint)(characterBase + SavemapPartyReader.ExperienceOffset), 1234);
        memory.WriteUInt32((uint)(characterBase + SavemapPartyReader.ExperienceToNextLevelOffset), 234);

        var computed = SavemapPartyReader.AddressComputedPartyData;
        memory.Write(
            (uint)(computed + SavemapPartyReader.ComputedStrengthOffset),
            [20, 21, 22, 23, 24, 25]);
        memory.WriteUInt16((uint)(computed + SavemapPartyReader.ComputedAttackOffset), 30);
        memory.WriteUInt16((uint)(computed + SavemapPartyReader.ComputedDefenseOffset), 31);
        memory.WriteUInt16((uint)(computed + SavemapPartyReader.ComputedMagicAttackOffset), 32);
        memory.WriteUInt16((uint)(computed + SavemapPartyReader.ComputedMagicDefenseOffset), 33);
        memory.WriteByte(
            (uint)(SavemapPartyReader.AddressWeaponAttackPercent + SavemapPartyReader.WeaponRecordSize),
            96);
        memory.Write(
            (uint)(SavemapPartyReader.AddressArmorDefensePercent + (2 * SavemapPartyReader.ArmorRecordSize)),
            [11, 4]);
        return memory;
    }

    private static StatusMenuSnapshot CreateStatus() =>
        new(
            PartySlot: 0,
            CharacterId: 0,
            Name: "Cloud",
            Level: 7,
            CurrentHp: 314,
            MaxHp: 314,
            CurrentMp: 54,
            MaxMp: 54,
            Strength: 17,
            Dexterity: 8,
            Vitality: 14,
            Magic: 13,
            Spirit: 12,
            Luck: 10,
            Attack: 22,
            AttackPercent: 96,
            Defense: 18,
            DefensePercent: 13,
            MagicAttack: 13,
            MagicDefense: 12,
            MagicDefensePercent: 4,
            Experience: 1250,
            ExperienceToNextLevel: 550,
            LimitLevel: 1,
            WeaponName: "Buster Sword",
            ArmorName: "Bronze Bangle",
            AccessoryName: "None");

    private static DateTime UtcNow() =>
        new(2026, 7, 20, 20, 0, 0, DateTimeKind.Utc);

    private static void Contains(string? actual, string expected, string label)
    {
        if (actual is null || !actual.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{label}: expected '{expected}' within '{actual ?? "<null>"}'.");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{label}: expected {expected}, got {actual}.");
        }
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];

        internal void Write(uint address, IReadOnlyList<byte> values)
        {
            for (var index = 0; index < values.Count; index++)
            {
                bytes[checked(address + (uint)index)] = values[index];
            }
        }

        internal void WriteByte(uint address, byte value) => bytes[address] = value;

        internal void WriteUInt16(uint address, ushort value)
        {
            Span<byte> encoded = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16LittleEndian(encoded, value);
            Write(address, encoded.ToArray());
        }

        internal void WriteInt32(uint address, int value)
        {
            Span<byte> encoded = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(encoded, value);
            Write(address, encoded.ToArray());
        }

        internal void WriteUInt32(uint address, uint value)
        {
            Span<byte> encoded = stackalloc byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(encoded, value);
            Write(address, encoded.ToArray());
        }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out destination[index]))
                {
                    destination.Clear();
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class TearingMemory(
        ILegacyAddressSpace first,
        ILegacyAddressSpace second,
        uint watchedAddress) : ILegacyAddressSpace
    {
        private int watchedReads;

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            if (virtualAddress == watchedAddress)
            {
                watchedReads++;
            }

            return (watchedReads < 2 ? first : second).TryRead(virtualAddress, destination);
        }
    }
}
