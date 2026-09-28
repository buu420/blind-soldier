using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The 2026-09-27 legacy session, 18:04:33-18:04:37: Item, then Key Items. The list's
/// widget (0x00DD1AC0, two columns by ten rows) moved its cursor across ten owned key items
/// and nothing was spoken - it was not in the widget catalog, so it fell to the generic
/// cursor match, which found no cursor. The frames here are the text that session drew.
/// FUN_00715105 draws each name at (column * 0x125 + 0x35, row * 0x24 + 0x7C) and the
/// selected item's description at (0x1B, 0x40), at the high-resolution layout.
/// </summary>
internal static class KeyItemsMenuSpeechTests
{
    private const int Context = 0x3DCED917;
    private const uint KeyItemsWidget = 0x00DD1AC0;

    private static readonly (string Name, uint X, uint Y)[] LoggedGrid =
    [
        ("Silk Dress", 53, 124), ("Blonde Wig", 346, 124),
        ("Diamond Tiara", 53, 160), ("Sexy Cologne", 346, 160),
        ("Member's Card", 53, 196), ("Bikini briefs", 346, 196),
        ("PHS", 53, 232), ("A Coupon", 346, 232),
        ("B Coupon", 53, 268), ("C Coupon", 346, 268)
    ];

    internal static void Run()
    {
        TheKeyItemsWidgetIsCatalogued();
        TheSelectedKeyItemIsSpokenWithItsDescription();
        TheLowResolutionLayoutIsReadToo();
        ABlankCellIsSaidOnlyWhenTheGridWasDrawn();
        AScrollingListWaitsForTheScrollToFinish();
        GreyedLimitLevelsAreSaidToBeUnavailable();
        TheLimitHeaderIsReadFromTheScreensOwnCharacter();
        TheWidgetReaderAttachesTheHeaderOnlyOnTheLimitScreen();
        TheCurrentLimitLevelIsSaidOnEntryAndOnChangeOnly();
        Console.WriteLine("PASS Key Items list: catalogued, spoken with descriptions at both layouts, blanks said only for a drawn grid; greyed Limit levels said to be unavailable; the Limit header's current level said on entry and change.");
    }

    private static void TheKeyItemsWidgetIsCatalogued()
    {
        Require(MenuWidgetCatalog.TryResolve(KeyItemsWidget, out var descriptor), "0x00DD1AC0 resolves in the widget catalog");
        Require(descriptor.Kind == MenuWidgetKind.KeyItemList, $"as the Key Items list, not {descriptor.Kind}");
        Require(descriptor.Name == "Key Items list", $"named \"{descriptor.Name}\"");
    }

    private static void TheSelectedKeyItemIsSpokenWithItsDescription()
    {
        var coordinator = new ActiveMenuFrameSpeechCoordinator();
        var now = new DateTime(2026, 9, 27, 18, 4, 33, DateTimeKind.Utc);
        DrawLoggedFrame(coordinator, "Dress made of silk", 27, 64);
        coordinator.CompleteFrame(Widget(column: 0, row: 0), now);
        Equal("Silk Dress. Dress made of silk", coordinator.Poll(), "18:04:33 first key item");

        DrawLoggedFrame(coordinator, "A blonde wig", 27, 64);
        coordinator.CompleteFrame(Widget(column: 1, row: 0), now.AddMilliseconds(500));
        Equal("Blonde Wig. A blonde wig", coordinator.Poll(), "18:04:34 right column");

        DrawLoggedFrame(coordinator, "A blonde wig", 27, 64);
        coordinator.CompleteFrame(Widget(column: 1, row: 0), now.AddMilliseconds(520));
        Equal<string?>(null, coordinator.Poll(), "an unchanged selection is not repeated");

        DrawLoggedFrame(coordinator, "Allows you to use the phone", 27, 64);
        coordinator.CompleteFrame(Widget(column: 0, row: 3), now.AddMilliseconds(900));
        Equal("PHS. Allows you to use the phone", coordinator.Poll(), "fourth row, left column");
    }

    private static void TheLowResolutionLayoutIsReadToo()
    {
        var coordinator = new ActiveMenuFrameSpeechCoordinator();
        // (column * 0x92 + 0x1B, row * 0x12 + 0x3E); description at (0x0E, 0x20).
        coordinator.ObserveDraw(new MenuTextRenderEntry("Silk Dress", 27, 62, 7, Context));
        coordinator.ObserveDraw(new MenuTextRenderEntry("Blonde Wig", 173, 62, 7, Context));
        coordinator.ObserveDraw(new MenuTextRenderEntry("Diamond Tiara", 27, 80, 7, Context));
        coordinator.ObserveDraw(new MenuTextRenderEntry("A diamond tiara", 14, 32, 7, Context));
        coordinator.CompleteFrame(Widget(column: 0, row: 1), DateTime.UtcNow);
        Equal("Diamond Tiara. A diamond tiara", coordinator.Poll(), "low-resolution layout");
    }

    private static void ABlankCellIsSaidOnlyWhenTheGridWasDrawn()
    {
        var coordinator = new ActiveMenuFrameSpeechCoordinator();
        DrawLoggedFrame(coordinator, null, 0, 0);
        coordinator.CompleteFrame(Widget(column: 0, row: 7), DateTime.UtcNow);
        Equal("Empty slot", coordinator.Poll(), "a blank cell below the owned key items");

        var incomplete = new ActiveMenuFrameSpeechCoordinator();
        incomplete.ObserveDraw(new MenuTextRenderEntry("Key Items", 243, 17, 7, Context));
        incomplete.CompleteFrame(Widget(column: 0, row: 0), DateTime.UtcNow);
        Equal<string?>(null, incomplete.Poll(), "a frame without the grid says nothing rather than a guessed blank");
    }

    private static void AScrollingListWaitsForTheScrollToFinish()
    {
        var coordinator = new ActiveMenuFrameSpeechCoordinator();
        DrawLoggedFrame(coordinator, "Dress made of silk", 27, 64);
        coordinator.CompleteFrame(Widget(column: 0, row: 0) with { ScrollDelta = -2, ScrollState = 2 }, DateTime.UtcNow);
        Equal<string?>(null, coordinator.Poll(), "mid-scroll rows are not read");
    }

    /// <summary>
    /// Main menu, Limit, Check - 18:33:55-18:34:18 in the same session: LEVEL 3 and 4 were
    /// drawn in colour 0 (FUN_0070212a: a level not yet reached; confirming it only plays
    /// the error sound) and were spoken as a bare "LEVEL 3", unlike the grey a sighted
    /// player sees.
    /// </summary>
    private static void GreyedLimitLevelsAreSaidToBeUnavailable()
    {
        const int levelContext = 0x3E99999A;
        const int nameContext = 0x3DCED917;
        void Draw(ActiveMenuFrameSpeechCoordinator coordinator)
        {
            coordinator.ObserveDraw(new MenuTextRenderEntry("LEVEL 1", 56, 193, 7, levelContext));
            coordinator.ObserveDraw(new MenuTextRenderEntry("[SWITCH]\"Braver", 75, 219, 7, nameContext));
            coordinator.ObserveDraw(new MenuTextRenderEntry("[SWITCH]\"Cross-slash", 75, 253, 7, nameContext));
            coordinator.ObserveDraw(new MenuTextRenderEntry("LEVEL 2", 348, 193, 7, levelContext));
            coordinator.ObserveDraw(new MenuTextRenderEntry("[SWITCH]\"Blade Beam", 368, 219, 7, nameContext));
            coordinator.ObserveDraw(new MenuTextRenderEntry("[SWITCH]\"Climhazzard", 368, 253, 7, nameContext));
            coordinator.ObserveDraw(new MenuTextRenderEntry("LEVEL 3", 56, 330, 0, levelContext));
            coordinator.ObserveDraw(new MenuTextRenderEntry("LEVEL 4", 348, 330, 0, levelContext));
        }

        ActiveMenuWidgetSnapshot Level(int column, int row) =>
            new(0x00DCA208, "Limit check level", MenuWidgetKind.LimitLevel, column, row, 2, 2, 0, 0, 0);

        var menu = new ActiveMenuFrameSpeechCoordinator();
        var now = new DateTime(2026, 9, 27, 18, 33, 55, DateTimeKind.Utc);
        Draw(menu);
        menu.CompleteFrame(Level(0, 0), now);
        Equal("LEVEL 1. Braver. Cross-slash", menu.Poll(), "a reached level keeps its Limit names");
        Draw(menu);
        menu.CompleteFrame(Level(0, 1), now.AddSeconds(1));
        Equal("LEVEL 3 unavailable", menu.Poll(), "a greyed level is said to be unavailable");
        Draw(menu);
        menu.CompleteFrame(Level(1, 1), now.AddSeconds(2));
        Equal("LEVEL 4 unavailable", menu.Poll(), "LEVEL 4 too");
    }

    private const uint LimitSlotAddress = 0x00DCA3C8;

    private static uint PartyAddress => (uint)(SavemapPartyReader.AddressSavemap + SavemapPartyReader.PartyMembersOffset);

    private static uint RecordAddress(int record) =>
        (uint)(SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset + record * SavemapPartyReader.CharacterSize);

    /// <summary>The 18:33-18:34 party: Cloud, Aeris, Barret; Cloud and Aeris at Limit level 1.</summary>
    private static GuestMemory LoggedParty(int limitSlot)
    {
        var memory = new GuestMemory();
        memory.WriteInt32(LimitSlotAddress, limitSlot);
        memory.Write(PartyAddress, [0, 3, 1]);
        foreach (var (record, name, level, gauge) in new[] { (0, "Cloud", 1, 102), (3, "Aeris", 1, 255), (1, "Barret", 2, 0) })
        {
            memory.WriteByte(RecordAddress(record), (byte)record);
            memory.Write(RecordAddress(record) + (uint)SavemapPartyReader.CharacterNameOffset,
                name.Select(c => (byte)(c - 0x20)).Concat(Enumerable.Repeat((byte)0xFF, 12 - name.Length)).ToArray());
            memory.WriteByte(RecordAddress(record) + (uint)SavemapPartyReader.LimitLevelOffset, (byte)level);
            memory.WriteByte(RecordAddress(record) + (uint)SavemapPartyReader.LimitGaugeOffset, (byte)gauge);
        }

        return memory;
    }

    /// <summary>
    /// FUN_0070212a/FUN_00703344 take the character from DAT_00dc0230[DAT_00dca3c8] (the party
    /// byte at the Limit screen's own slot, set by FUN_007020a0 from the main menu's party
    /// cursor and stepped by L1/R1) and its record through DAT_00919928 (identity for 0..8,
    /// 9 and 10 to 6 and 7); the header prints record +0x0E and draws the bar from +0x0F.
    /// </summary>
    private static void TheLimitHeaderIsReadFromTheScreensOwnCharacter()
    {
        var memory = LoggedParty(limitSlot: 1);
        Require(new LimitMenuHeaderReader(memory).TryRead(out var aeris), "the header of slot 1 reads");
        Equal(new LimitMenuHeaderSnapshot(1, 3, 3, 1, 255, "Aeris"), aeris, "slot 1 is Aeris, level 1, gauge full");
        Require(aeris.IsGaugeFull, "a 0xFF bar is full");

        memory.WriteInt32(LimitSlotAddress, 0);
        Require(new LimitMenuHeaderReader(memory).TryRead(out var cloud) && cloud.CharacterId == 0 && cloud.Name == "Cloud",
            "the screen's slot, not the first party member, chooses the character");
        Equal(40, cloud.GaugePercent, "102 of 255 is 40 percent of the bar");

        memory.Write(PartyAddress, [9, 3, 1]);
        memory.WriteByte(RecordAddress(6) + (uint)SavemapPartyReader.LimitLevelOffset, 3);
        memory.WriteByte(RecordAddress(6) + (uint)SavemapPartyReader.LimitGaugeOffset, 0);
        Require(new LimitMenuHeaderReader(memory).TryRead(out var scripted) && scripted.RecordIndex == 6 && scripted.LimitLevel == 3,
            "party id 9 is read from record 6, as DAT_00919928 maps it");

        memory.Write(PartyAddress, [0, 3, 1]);
        memory.WriteInt32(LimitSlotAddress, 3);
        Require(!new LimitMenuHeaderReader(memory).TryRead(out _), "a slot outside the party reads nothing");
        memory.WriteInt32(LimitSlotAddress, 1);
        memory.WriteByte(RecordAddress(3) + (uint)SavemapPartyReader.LimitLevelOffset, 0);
        Require(!new LimitMenuHeaderReader(memory).TryRead(out _), "a level outside 1..4 is not a header");
    }

    private static void TheWidgetReaderAttachesTheHeaderOnlyOnTheLimitScreen()
    {
        var memory = LoggedParty(limitSlot: 0);
        void WriteWidget(uint address, int first, int cursor, int columns, int rows)
        {
            memory.WriteInt32(address, first);
            memory.WriteInt32(address + 4, cursor);
            memory.WriteInt32(address + 8, columns);
            memory.WriteInt32(address + 0x0C, rows);
            memory.WriteInt32(address + 0x14, 0);
            memory.WriteInt32(address + 0x24, 0);
            memory.WriteInt32(address + 0x30, 0);
        }

        WriteWidget(0x00DCA1D0, 1, 0, 2, 1);
        WriteWidget(0x00DD1A50, 0, 0, 1, 10);
        var reader = new ActiveMenuWidgetReader(memory);
        Require(reader.TryRead(0x00DCA1D0u, out var command) && command.LimitHeader is { CharacterId: 0, LimitLevel: 1 },
            "the Limit command widget carries Cloud's header");
        Require(reader.TryRead(0x00DD1A50u, out var items) && items.LimitHeader is null,
            "an Item list never reads the Limit screen's slot");
    }

    private static void TheCurrentLimitLevelIsSaidOnEntryAndOnChangeOnly()
    {
        var cloud = new LimitMenuHeaderSnapshot(0, 0, 0, 1, 102, "Cloud");
        var aeris = new LimitMenuHeaderSnapshot(1, 3, 3, 1, 255, "Aeris");
        ActiveMenuWidgetSnapshot Command(int first, LimitMenuHeaderSnapshot header) =>
            new(0x00DCA1D0, "Limit command", MenuWidgetKind.LimitCommand, first, 0, 2, 1, 0, 0, 0, LimitHeader: header);
        ActiveMenuWidgetSnapshot Level(int column, LimitMenuHeaderSnapshot header) =>
            new(0x00DCA208, "Limit check level", MenuWidgetKind.LimitLevel, column, 0, 2, 2, 0, 0, 0, LimitHeader: header);
        void DrawLevels(ActiveMenuFrameSpeechCoordinator coordinator)
        {
            coordinator.ObserveDraw(new MenuTextRenderEntry("LEVEL 1", 56, 193, 7, 0x3E99999A));
            coordinator.ObserveDraw(new MenuTextRenderEntry("[SWITCH]\"Braver", 75, 219, 7, 0x3DCED917));
            coordinator.ObserveDraw(new MenuTextRenderEntry("LEVEL 2", 348, 193, 0, 0x3E99999A));
        }

        var menu = new ActiveMenuFrameSpeechCoordinator();
        var now = new DateTime(2026, 9, 27, 18, 33, 52, DateTimeKind.Utc);
        menu.CompleteFrame(new ActiveMenuWidgetSnapshot(0x00DC1188, "Main menu party", MenuWidgetKind.CharacterList, 0, 0, 1, 3, 0, 0, 0,
            NativeSelection: new NativeMenuSelection("Cloud", null, "party:00DC1188:0:0:Cloud")), now);
        Equal("Cloud", menu.Poll(), "18:33:52 choosing Cloud");

        menu.CompleteFrame(Command(0, cloud), now.AddSeconds(2));
        Equal("Limit level 1. Limit gauge 40 percent. Set", menu.Poll(), "18:33:54 entering the Limit screen says the current level");
        menu.CompleteFrame(Command(0, cloud), now.AddSeconds(2.02));
        Equal<string?>(null, menu.Poll(), "the next frame must not interrupt the header with a second Set");
        menu.CompleteFrame(Command(1, cloud), now.AddSeconds(2.2));
        Equal("Check", menu.Poll(), "a cursor move does not repeat it");
        DrawLevels(menu);
        menu.CompleteFrame(Level(0, cloud), now.AddSeconds(3));
        Equal("LEVEL 1. Braver", menu.Poll(), "nor does entering Check");

        menu.CompleteFrame(Command(1, aeris), now.AddSeconds(5));
        Equal("Aeris. Limit level 1. Limit gauge full. Check", menu.Poll(), "L1/R1 to Aeris names her and says her level");
        menu.CompleteFrame(Command(1, aeris), now.AddSeconds(5.02));
        Equal<string?>(null, menu.Poll(), "the character header must survive the next frame");

        var aerisSet = aeris with { LimitLevel = 2 };
        menu.CompleteFrame(Command(0, aerisSet), now.AddSeconds(7));
        Equal("Limit level 2. Limit gauge full. Set", menu.Poll(), "Set changing the level says the new level");
        menu.CompleteFrame(Command(1, aerisSet), now.AddSeconds(7.5));
        Equal("Check", menu.Poll(), "and only once");

        menu.CompleteFrame(new ActiveMenuWidgetSnapshot(0x00DC1150, "Item/Main list", MenuWidgetKind.RootMainMenu, 0, 6, 1, 11, 0, 0, 0), now.AddSeconds(9));
        menu.CompleteFrame(Command(0, aerisSet), now.AddSeconds(12));
        Equal("Limit level 2. Limit gauge full. Set", menu.Poll(), "coming back into the screen says it again");
        menu.CompleteFrame(Command(0, aerisSet), now.AddSeconds(12.02));
        Equal<string?>(null, menu.Poll(), "an unchanged returning screen is quiet");
        menu.CompleteFrame(new ActiveMenuWidgetSnapshot(0x00DC1150, "Item/Main list", MenuWidgetKind.RootMainMenu, 0, 6, 1, 11, 0, 0, 0), now.AddSeconds(12.04));
        menu.CompleteFrame(Command(0, aerisSet), now.AddSeconds(12.06));
        Equal("Limit level 2. Limit gauge full. Set", menu.Poll(), "quick re-entry with the same cursor still reads the header");

        var unread = new ActiveMenuFrameSpeechCoordinator();
        unread.CompleteFrame(new ActiveMenuWidgetSnapshot(0x00DCA1D0, "Limit command", MenuWidgetKind.LimitCommand, 0, 0, 2, 1, 0, 0, 0), now);
        Equal("Set", unread.Poll(), "without a checked header the command is still said, with no guessed level");
    }

    /// <summary>A sparse little-endian guest address space for the Limit header fixtures.</summary>
    private sealed class GuestMemory : Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out var value))
                {
                    return false;
                }

                destination[index] = value;
            }

            return true;
        }

        public void WriteByte(uint address, byte value) => bytes[address] = value;

        public void Write(uint address, IReadOnlyList<byte> values)
        {
            for (var index = 0; index < values.Count; index++)
            {
                bytes[address + (uint)index] = values[index];
            }
        }

        public void WriteInt32(uint address, int value) => Write(address, BitConverter.GetBytes(value));
    }

    private static void DrawLoggedFrame(ActiveMenuFrameSpeechCoordinator coordinator, string? description, uint x, uint y)
    {
        coordinator.ObserveDraw(new MenuTextRenderEntry("Item", 508, 13, 7, 0x3A83126F));
        foreach (var (name, nameX, nameY) in LoggedGrid)
        {
            coordinator.ObserveDraw(new MenuTextRenderEntry(name, nameX, nameY, 7, Context));
        }

        coordinator.ObserveDraw(new MenuTextRenderEntry("Use", 57, 17, 7, Context));
        coordinator.ObserveDraw(new MenuTextRenderEntry("Arrange", 150, 17, 7, Context));
        coordinator.ObserveDraw(new MenuTextRenderEntry("Key Items", 243, 17, 7, Context));
        if (description is not null)
        {
            coordinator.ObserveDraw(new MenuTextRenderEntry(description, x, y, 7, Context));
        }
    }

    private static ActiveMenuWidgetSnapshot Widget(int column, int row) =>
        new(KeyItemsWidget, "Key Items list", MenuWidgetKind.KeyItemList, column, row, 2, 10, 0, 0, 0);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected \"{expected}\", got \"{actual}\".");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
