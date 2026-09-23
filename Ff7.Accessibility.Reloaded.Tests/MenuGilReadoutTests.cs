using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Shared by the x86 and x64 suites. The balance is read where FFVII draws it:
/// the settled root menu (FUN_006CA346) and the shop screens that draw the
/// wallet (FUN_0071AAA3 states 1, 3, 4 and 5).
/// </summary>
internal static class MenuGilReadoutTests
{
    // Independent of the reader constants on purpose.
    private const int Gil = 0x00DC08B4;
    private const int Session = 0x00DC12F0;
    private const int Phase = 0x00DC1298;
    private const int Module = 0x00CBF9DC;
    private const int ShopActive = 0x00DD4734;
    private const int ShopState = 0x0092565C;

    internal static void Run()
    {
        ReaderUsesTheNativeWalletAndSessionAddresses();
        ReadsTheBalanceCoherentlyAndKeepsZero();
        MainMenuSessionFollowsTheNativeDriverPhase();
        RootBalanceNeedsTheSettledRootScreen();
        ShopBalanceMatchesTheNativeWalletStates();
        OpeningIsAnnouncedOncePerMainMenuSession();
        OpeningFollowsTheFirstSelectionAndNeverSpeaksAFailedRead();
        RepeatSpeaksTheCurrentVisibleBalance();
        HeldKeyCannotBecomeAPressAcrossFocusOrMenuChanges();
        ClosingOrUnreadableRootSessionBlocksRepeat();
        LosingTheMenuDuringTheBalanceReadStaysSilent();
        EmptyShopRowsStillAnnounceTheVisibleWallet();
    }

    private static void ReaderUsesTheNativeWalletAndSessionAddresses()
    {
        Equal(Gil, MenuGilStateReader.AddressGil, "savemap wallet at 0x0B7C");
        Equal(Session, MenuGilStateReader.AddressMainMenuSession, "FUN_006CB56A session flag");
        Equal(Phase, MenuGilStateReader.AddressMainMenuPhase, "FUN_006CA346 slide phase");
        Equal(Module, FieldPositionReader.AddressCurrentModule, "module byte used by the shop envelope");
        Equal(0x47, MenuGilReadoutController.VirtualKeyG, "gil repeat uses the G key");
        Equal("4294967295 gil.", MenuGilReadoutController.Format(uint.MaxValue), "full unsigned balance");
    }

    private static void ReadsTheBalanceCoherentlyAndKeepsZero()
    {
        var memory = new Memory();
        var reader = new MenuGilStateReader(memory);
        memory.UInt32(Gil, 12345);
        Equal(true, reader.TryReadGil(out var gil), "coherent balance read");
        Equal(12345u, gil, "native balance value");

        memory.UInt32(Gil, 0);
        Equal(true, reader.TryReadGil(out gil), "zero balance is a real balance");
        Equal(0u, gil, "zero balance value");

        var torn = new Memory();
        torn.UInt32(Gil, 100);
        torn.BeforeRead = (address, count) =>
        {
            if (address == Gil && count == 2)
            {
                torn.UInt32(Gil, 50);
            }
        };
        Equal(false, new MenuGilStateReader(torn).TryReadGil(out _), "a balance changing mid-read is rejected");

        var unreadable = new Memory();
        Equal(false, new MenuGilStateReader(unreadable).TryReadGil(out gil), "unreadable balance");
        Equal(0u, gil, "a failed read reports no value");
    }

    private static void MainMenuSessionFollowsTheNativeDriverPhase()
    {
        foreach (var (session, phase, open, label) in new[]
                 {
                     (1, 0, true, "sliding open"),
                     (1, 1, true, "open"),
                     (1, 2, false, "sliding closed"),
                     (1, -1, false, "closed"),
                     (0, 1, false, "driver not running"),
                     (0, 0, false, "no session")
                 })
        {
            var memory = new Memory();
            memory.Int32(Session, session);
            memory.Int32(Phase, phase);
            Equal(true, new MenuGilStateReader(memory).TryReadMainMenuSessionOpen(out var actual), $"{label} is readable");
            Equal(open, actual, $"{label} session phase");
        }

        var torn = new Memory();
        torn.Int32(Session, 1);
        torn.Int32(Phase, 1);
        torn.BeforeRead = (address, count) =>
        {
            if (address == Phase && count == 2)
            {
                torn.Int32(Phase, 2);
            }
        };
        Equal(false, new MenuGilStateReader(torn).TryReadMainMenuSessionOpen(out _), "torn session phase answers nothing");
        Equal(false, new MenuGilStateReader(new Memory()).TryReadMainMenuSessionOpen(out _), "unreadable session answers nothing");
    }

    private static void RootBalanceNeedsTheSettledRootScreen()
    {
        var root = RootSnapshot();
        Equal(true, MenuGilStateReader.IsMainMenuBalanceVisible(true, root, false), "settled root draws the balance");
        Equal(false, MenuGilStateReader.IsMainMenuBalanceVisible(false, root, false), "root without render ownership");
        Equal(false, MenuGilStateReader.IsMainMenuBalanceVisible(true, null, false), "unreadable main menu");
        Equal(false, MenuGilStateReader.IsMainMenuBalanceVisible(true, root, true), "Quit dialog is not the root screen");
        Equal(false, MenuGilStateReader.IsMainMenuBalanceVisible(true, root with { Target = 1 }, false),
            "an old render lease cannot vouch for the Item submenu");
        foreach (var state in new[] { 0, 2, 3, 4, 5, 6 })
        {
            Equal(false, MenuGilStateReader.IsMainMenuBalanceVisible(true, root with { State = state }, false),
                $"command column state {state} is not the settled root");
        }

        Equal(false, MenuGilStateReader.IsMainMenuBalanceVisible(true, root with { MenuOpen = 0 }, false), "closed menu");
        Equal(false, MenuGilStateReader.IsMainMenuBalanceVisible(true, root with { EnabledMask = 0 }, false), "no drawn rows");
        Equal(MenuGilScreen.Shop, MenuGilStateReader.ResolveScreen(true, true), "shop wallet wins");
        Equal(MenuGilScreen.MainMenu, MenuGilStateReader.ResolveScreen(false, true), "root wallet");
        Equal(MenuGilScreen.None, MenuGilStateReader.ResolveScreen(false, false), "no wallet on screen");
    }

    private static void ShopBalanceMatchesTheNativeWalletStates()
    {
        for (var state = 0; state < 7; state++)
        {
            var memory = ShopMemory(state);
            Equal(true, new ShopMenuStateReader(memory).TryReadBalanceVisibility(out var visible), $"shop state {state} readable");
            Equal(state is 1 or 3 or 4 or 5, visible, $"shop state {state} wallet visibility");
        }

        var field = ShopMemory(1);
        field.Byte(Module, 1);
        Equal(true, new ShopMenuStateReader(field).TryReadBalanceVisibility(out var fieldVisible), "field module readable");
        Equal(false, fieldVisible, "stale shop globals outside the shop module");

        var fading = ShopMemory(1);
        fading.Int32(ShopActive, 2);
        Equal(true, new ShopMenuStateReader(fading).TryReadBalanceVisibility(out var fadingVisible), "closing shop readable");
        Equal(false, fadingVisible, "a shop that is closing does not own the wallet");

        var torn = ShopMemory(1);
        torn.BeforeRead = (address, count) =>
        {
            if (address == ShopState && count == 2)
            {
                torn.Int32(ShopState, 2);
            }
        };
        Equal(false, new ShopMenuStateReader(torn).TryReadBalanceVisibility(out _), "torn shop state answers nothing");
        Equal(false, new ShopMenuStateReader(new Memory()).TryReadBalanceVisibility(out _), "unreadable shop");
    }

    private static void OpeningIsAnnouncedOncePerMainMenuSession()
    {
        var controller = new MenuGilReadoutController();
        var output = new Output();
        uint balance = 1000;
        var screen = MenuGilScreen.MainMenu;
        MenuGilReadoutResult Tick(bool? session, bool delivered = true, bool repeat = false) =>
            controller.Tick(repeat, session, delivered, () => screen, () => balance, output.Speak);

        Equal(MenuGilReadoutOutcome.Announced, Tick(true).Outcome, "opening the main menu announces the balance");
        Equal("1000 gil.|queued", output.Single(), "opening balance queues after the first selection");
        Equal(MenuGilReadoutOutcome.None, Tick(true).Outcome, "the next poll does not repeat it");
        Equal(MenuGilReadoutOutcome.None, Tick(true).Outcome, "moving the root cursor does not repeat it");

        screen = MenuGilScreen.None;
        Equal(MenuGilReadoutOutcome.None, Tick(true, delivered: false).Outcome, "Item submenu is silent");
        screen = MenuGilScreen.MainMenu;
        Equal(MenuGilReadoutOutcome.None, Tick(true).Outcome, "returning from a submenu is the same opening");
        Equal(0, output.Count, "nothing further was spoken during the session");

        Equal(MenuGilReadoutOutcome.None, Tick(null).Outcome, "an unreadable phase neither ends nor repeats");
        Equal(MenuGilReadoutOutcome.None, Tick(true).Outcome, "an unreadable phase did not end the session");

        Equal(MenuGilReadoutOutcome.None, Tick(false).Outcome, "closing the menu speaks nothing");
        balance = 640;
        Equal(MenuGilReadoutOutcome.Announced, Tick(true).Outcome, "reopening announces again");
        Equal("640 gil.|queued", output.Single(), "the reopened balance is read fresh");
    }

    private static void OpeningFollowsTheFirstSelectionAndNeverSpeaksAFailedRead()
    {
        var controller = new MenuGilReadoutController();
        var output = new Output();
        uint? balance = 300;
        var reads = 0;
        var screenReads = 0;
        uint? ReadGil()
        {
            reads++;
            return balance;
        }

        MenuGilScreen ReadScreen()
        {
            screenReads++;
            return MenuGilScreen.MainMenu;
        }

        Equal(MenuGilReadoutOutcome.None,
            controller.Tick(false, true, false, ReadScreen, ReadGil, output.Speak).Outcome,
            "the balance waits for the first root selection");
        Equal(0, reads + screenReads, "nothing is read before it is due");

        balance = null;
        Equal(MenuGilReadoutOutcome.None,
            controller.Tick(false, true, true, ReadScreen, ReadGil, output.Speak).Outcome,
            "a failed read is not spoken");
        Equal(0, output.Count, "a failed read never becomes zero gil");

        output.Accept = false;
        balance = 0;
        Equal(MenuGilReadoutOutcome.SpeechRejected,
            controller.Tick(false, true, true, ReadScreen, ReadGil, output.Speak).Outcome,
            "a refused announcement is reported");
        output.Accept = true;
        Equal(MenuGilReadoutOutcome.Announced,
            controller.Tick(false, true, true, ReadScreen, ReadGil, output.Speak).Outcome,
            "the announcement is retried while the root is still up");
        Equal("0 gil.|queued", output.Single(), "a real zero balance is spoken");

        var submenu = new MenuGilReadoutController();
        Equal(MenuGilReadoutOutcome.None,
            submenu.Tick(false, true, true, () => MenuGilScreen.None, ReadGil, output.Speak).Outcome,
            "no opening announcement while a submenu is up");
        Equal(0, output.Count, "submenu stayed silent");
    }

    private static void RepeatSpeaksTheCurrentVisibleBalance()
    {
        var controller = new MenuGilReadoutController();
        var output = new Output();
        uint? balance = 500;
        var screen = MenuGilScreen.Shop;
        var reads = 0;
        MenuGilReadoutResult Press() =>
            controller.Tick(true, null, false, () => screen, () =>
            {
                reads++;
                return balance;
            }, output.Speak);

        var shop = Press();
        Equal(MenuGilReadoutOutcome.Repeated, shop.Outcome, "G on a shop wallet screen");
        Equal(MenuGilScreen.Shop, shop.Screen, "shop wallet screen");
        Equal("500 gil.|interrupt", output.Single(), "G interrupts with the balance");

        balance = 450;
        Press();
        Equal("450 gil.|interrupt", output.Single(), "G after a purchase reads the new balance");

        screen = MenuGilScreen.None;
        reads = 0;
        Equal(MenuGilReadoutOutcome.RepeatNotVisible, Press().Outcome, "G with no wallet on screen");
        Equal(0, reads, "no hidden balance is read off screen");
        Equal(0, output.Count, "G off screen is silent");

        screen = MenuGilScreen.Shop;
        balance = null;
        Equal(MenuGilReadoutOutcome.ReadFailed, Press().Outcome, "G with an unreadable balance");
        Equal(0, output.Count, "a failed read is not spoken as zero");

        balance = 0;
        Press();
        Equal("0 gil.|interrupt", output.Single(), "G speaks a real zero balance");

        var root = new MenuGilReadoutController();
        Equal(MenuGilReadoutOutcome.Repeated,
            root.Tick(true, true, false, () => MenuGilScreen.MainMenu, () => 75u, output.Speak).Outcome,
            "G on the root before the opening announcement");
        Equal("75 gil.|interrupt", output.Single(), "root G speaks the balance");
        Equal(MenuGilReadoutOutcome.None,
            root.Tick(false, true, true, () => MenuGilScreen.MainMenu, () => 75u, output.Speak).Outcome,
            "the opening announcement does not follow a G answer");
        Equal(0, output.Count, "no duplicate balance");
    }

    private static void HeldKeyCannotBecomeAPressAcrossFocusOrMenuChanges()
    {
        var keys = new NavigationKeyPressTracker();
        var controller = new MenuGilReadoutController();
        var output = new Output();
        var screen = MenuGilScreen.None;
        void Sample(bool down, bool foreground) =>
            controller.Tick(
                keys.Observe(MenuGilReadoutController.VirtualKeyG, down, foreground),
                screen == MenuGilScreen.MainMenu ? true : null,
                false,
                () => screen,
                () => 90u,
                output.Speak);

        Sample(down: true, foreground: false);
        Sample(down: true, foreground: true);
        Equal(0, output.Count, "G held while focus returns is not a press");

        Sample(down: false, foreground: true);
        Sample(down: true, foreground: true);
        Equal(0, output.Count, "G pressed off screen is silent");
        screen = MenuGilScreen.MainMenu;
        Sample(down: true, foreground: true);
        Equal(0, output.Count, "G held while the menu opens is not a press");

        Sample(down: false, foreground: true);
        Sample(down: true, foreground: true);
        Equal("90 gil.|interrupt", output.Single(), "a fresh G press in the menu speaks");
    }

    private static void ClosingOrUnreadableRootSessionBlocksRepeat()
    {
        foreach (bool? session in new bool?[] { false, null })
        {
            var output = new Output();
            var controller = new MenuGilReadoutController();
            var result = controller.Tick(true, session, false,
                () => MenuGilScreen.MainMenu, () => 300u, output.Speak);
            Equal(MenuGilReadoutOutcome.RepeatNotVisible, result.Outcome,
                "retained render evidence cannot override a closed or unreadable session");
            Equal(0, output.Count, "no stale root balance after closing");
        }
    }

    private static void LosingTheMenuDuringTheBalanceReadStaysSilent()
    {
        foreach (var repeat in new[] { false, true })
        {
            var output = new Output();
            var controller = new MenuGilReadoutController();
            var screen = MenuGilScreen.MainMenu;
            controller.Tick(repeat, true, true, () => screen,
                () => { screen = MenuGilScreen.None; return 300u; }, output.Speak);
            Equal(0, output.Count, "closing during a balance read cancels pending speech");
            screen = MenuGilScreen.MainMenu;
            Equal(MenuGilReadoutOutcome.Announced,
                controller.Tick(false, true, true, () => screen, () => 250u, output.Speak).Outcome,
                "an undelivered announcement can still be delivered on a valid menu");
            Equal("250 gil.|queued", output.Single(), "retry uses the current visible amount");
        }
    }

    private static void EmptyShopRowsStillAnnounceTheVisibleWallet()
    {
        var memory = ShopMemory(3);
        memory.Int32(ShopMenuStateReader.AddressSellMateriaCursor, 0);
        memory.Int32(ShopMenuStateReader.AddressSellMateriaScroll, 0);
        memory.UInt32(MateriaMenuSelectionReader.AddressMateriaInventory, uint.MaxValue);
        memory.UInt32(Gil, 600);
        var reader = new ShopMenuStateReader(memory);
        var tracker = new ShopMenuSpeechTracker();
        Equal("600 gil.", tracker.Poll(reader), "empty materia list still draws a wallet");
        Equal<string?>(null, tracker.Poll(reader), "empty rows do not repeat the wallet every poll");

        memory.Int32(ShopState, 0);
        memory.Int32(ShopMenuStateReader.AddressTopCommandWidget, 0);
        Equal("Buy", tracker.Poll(reader), "top choice hides the wallet");
        memory.UInt32(Gil, 0);
        memory.Int32(ShopState, 3);
        Equal("0 gil.", tracker.Poll(reader), "reentering reads even a zero wallet fresh");

        var torn = ShopMemory(3);
        torn.UInt32(Gil, 900);
        torn.BeforeRead = (address, _) =>
        {
            if (address == Gil) torn.Int32(ShopState, 0);
        };
        Equal(false, new ShopMenuStateReader(torn).TryReadVisibleBalance(out _),
            "wallet read rejected when the shop hides it mid-read");
        Equal<string?>(null, new ShopMenuSpeechTracker().Poll(new ShopMenuStateReader(ShopMemory(3))),
            "an unreadable shop wallet is not spoken as zero");
    }

    private static MainMenuSnapshot RootSnapshot() =>
        new(
            State: 1,
            SelectedA: 0,
            SelectedB: 0,
            CursorIndex: 0,
            Target: 0,
            MenuOpen: 1,
            EnabledMask: 0x7ff,
            DisabledMask: 0,
            Animation: 0);

    private static Memory ShopMemory(int state)
    {
        var memory = new Memory();
        memory.Byte(Module, ShopMenuStateReader.ShopModule);
        memory.Int32(ShopActive, 1);
        memory.Int32(ShopState, state);
        return memory;
    }

    private static void Equal<T>(T expected, T actual, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{context}: expected {expected}, got {actual}");
        }
    }

    private sealed class Output
    {
        private readonly List<string> spoken = new();

        internal bool Accept { get; set; } = true;

        internal int Count => spoken.Count;

        internal bool Speak(string text, bool interrupt)
        {
            if (!Accept)
            {
                return false;
            }

            spoken.Add($"{text}|{(interrupt ? "interrupt" : "queued")}");
            return true;
        }

        internal string Single()
        {
            if (spoken.Count != 1)
            {
                throw new InvalidOperationException(
                    $"expected one utterance, got {spoken.Count}: {string.Join(" / ", spoken)}");
            }

            var text = spoken[0];
            spoken.Clear();
            return text;
        }
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<int, byte> bytes = new();
        private readonly Dictionary<int, int> reads = new();

        internal Action<int, int>? BeforeRead { get; set; }

        internal void Byte(int address, byte value) => bytes[address] = value;

        internal void Int32(int address, int value) => Write(address, BitConverter.GetBytes(value));

        internal void UInt32(int address, uint value) => Write(address, BitConverter.GetBytes(value));

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            var address = checked((int)virtualAddress);
            reads.TryGetValue(address, out var count);
            reads[address] = count + 1;
            BeforeRead?.Invoke(address, count + 1);
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(address + index, out var value))
                {
                    return false;
                }

                destination[index] = value;
            }

            return true;
        }

        private void Write(int address, byte[] value)
        {
            for (var index = 0; index < value.Length; index++)
            {
                bytes[address + index] = value[index];
            }
        }
    }
}
