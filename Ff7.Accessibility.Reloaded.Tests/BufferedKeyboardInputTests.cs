using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class BufferedKeyboardInputTests
{
    internal static void Run()
    {
        long time = 100;
        long? owner = 3;
        var foreground = true;
        var down = false;
        var fault = false;
        var diagnostics = new List<string>();
        using var input = new BufferedKeyboardInput(() => foreground,
            key => fault ? throw new InvalidOperationException("test read failed") : key == 'K' && down,
            () => owner, diagnostics.Add, startObserver: false, monotonicClock: () => time, ticksPerSecond: 1000);
        void Sample(bool pressed) { time += 10; down = pressed; input.Capture(); }
        void Tap() { Sample(true); Sample(false); }
        Tap();
        Check(input.TakePress('K', true), "a completed tap remains available after its physical release");
        Check(!input.TakePress('K', true), "completed tap is not repeated");
        Sample(true);
        Check(input.TakePress('K', true), "held key begins once");
        Sample(true);
        Check(!input.TakePress('K', true), "held key never becomes another press");
        Sample(false); Tap(); Tap();
        Check(input.TakePress('K', true) && input.TakePress('K', true) && !input.TakePress('K', true),
            "two separate taps between worker drains remain two commands");
        Tap(); foreground = false; input.Capture(); foreground = true; input.Capture();
        Check(!input.TakePress('K', true), "focus round trip clears pending commands");
        Tap(); owner = 1; input.Capture(); owner = 3; input.Capture();
        Check(!input.TakePress('K', true), "native owner round trip clears pending commands");
        Sample(true); owner = null; input.Capture(); owner = 3; input.Capture();
        Check(!input.TakePress('K', true), "a held key through unreadable context does not rearm");
        Sample(false); Tap(); time += 2001;
        Check(!input.TakePress('K', true), "a press older than two monotonic seconds is retired");
        Tap(); fault = true; input.Capture(); fault = false; input.Capture();
        Check(!input.TakePress('K', true), "read failure retires queued input");
        Tap(); Check(!input.TakePress('K', false) && !input.TakePress('K', true),
            "a disallowed owner's key cannot be replayed later");
        for (var i = 0; i < 9; i++) Tap();
        var consumed = 0;
        while (input.TakePress('K', true)) consumed++;
        Check(consumed == 8 && diagnostics.Any(d => d.Contains("overflow", StringComparison.Ordinal)),
            "bounded overflow retires one oldest press and is diagnosed");
        Tap(); input.Dispose();
        Check(!input.TakePress('K', true), "dispose clears pending keys before retiring native reads");
        NativeContextUsesOnlyItsOwningDomain();
        NavigationAndBattleCommandsKeepCapturedOrder();
        PendingNavigationBypassesLongScanIntervalsWithoutConsumingInput();
        QueuedCommandsKeepTheWorkerAwakeUntilDrained();
        NativeActivityOwnershipRetiresNavigationBacklogs();
        NativeWorldOwnershipRetiresNavigationBacklogs();
        PhysicalActionsRebaselineAcrossBufferedDomains();
        ObserverRetainsPressesWithoutAnAccessibilityWorkerPoll();
        Console.WriteLine("shared buffered keyboard deadline, fault, overflow and native-context checks passed.");
    }

    private static void PhysicalActionsRebaselineAcrossBufferedDomains()
    {
        long owner = 1L << 24;
        var down = false;
        using var input = new BufferedKeyboardInput(() => true, key => key == 'I' && down,
            () => owner, startObserver: false);
        down = true; input.Capture(); Check(input.TakePress('I', true), "ordinary buffered navigation begins once");
        owner = 10L << 24;
        Check(!input.ObservePhysicalAction('I', true), "a held navigation key cannot start pursuit in another native module");
        down = false; input.Capture(); input.ObservePhysicalAction('I', true); down = true;
        Check(input.ObservePhysicalAction('I', true) && !input.ObservePhysicalAction('I', true), "a fresh physical pursuit action remains one edge");
        owner = 1L << 24; input.Capture(); down = false; input.Capture(); owner = 9L << 24; input.Capture(); down = true;
        Check(input.ObservePhysicalAction('I', true), "a release in a buffered domain rearms a fresh Condor action");
    }

    private static void NativeWorldOwnershipRetiresNavigationBacklogs()
    {
        var memory = new HeaderMemory { Module = 3, AllowedScope = 3 };
        var blocked = false;
        var down = new HashSet<int>();
        var context = new KeyboardInputContextReader(memory, () => (nint)10, () => false, null, () => blocked);
        using var input = new BufferedKeyboardInput(() => true, down.Contains, context.Read,
            startObserver: false, keyGroups: KeyboardInputContextReader.OrderedKeys);
        void Tap(int key) { down.Add(key); input.Capture(); down.Remove(key); input.Capture(); }
        blocked = true; input.Capture(); Tap('I'); Tap('L'); Tap('J');
        blocked = false;
        Check(!input.TakePress('I', true), "same-map native world ownership recovery retires the buffered backlog");
        input.Capture(); Tap('R');
        Check(input.TakePress('R', true), "world readout remains available after native control recovery");
    }

    private static void NativeActivityOwnershipRetiresNavigationBacklogs()
    {
        var memory = new HeaderMemory { Module = 1, AllowedScope = 1 };
        var blocked = false;
        var settings = false;
        var down = new HashSet<int>();
        var context = new KeyboardInputContextReader(memory, () => (nint)10, () => settings, () => blocked);
        using var input = new BufferedKeyboardInput(() => true, down.Contains, context.Read,
            startObserver: false, keyGroups: KeyboardInputContextReader.OrderedKeys);
        void Tap(int key) { down.Add(key); input.Capture(); down.Remove(key); input.Capture(); }
        Tap('I'); blocked = true; input.Capture(); Tap('I'); Tap('L'); Tap('J');
        input.DiscardPresses(KeyboardInputContextReader.NavigationVirtualKeys, KeyboardInputContextReader.IsNormalFieldOwner);
        Check(!input.HasPendingPresses(KeyboardInputContextReader.NavigationVirtualKeys),
            "a blocked native field decision retires the entire navigation backlog");
        Tap('I'); Tap('L'); Tap('J');
        blocked = false;
        Check(!input.TakePress('I', true), "native activity exit in the same field retires its whole navigation backlog");
        input.Capture(); input.BeginWorkerFrame();
        Check(!input.TakePress('L', true) && !input.TakePress('J', true), "remaining activity keys cannot replay after exit");
        blocked = true; input.Capture();
        Tap('K'); input.BeginWorkerFrame();
        Check(input.TakePress('K', true), "the native activity's own status readout remains eligible before navigation retirement");
        Tap('R'); Tap(0x7A);
        Check(input.TakePress('R', true) && input.TakeOldestPress([0x7A], true) == 0x7A,
            "repeat and F11 remain available while a native activity owns navigation");
        down.Add('I'); input.Capture(); blocked = false; input.Capture(); input.BeginWorkerFrame();
        Check(!input.TakePress('I', true), "holding a navigation key through native activity recovery does not rearm it");
        down.Remove('I'); input.Capture(); Tap('I'); input.BeginWorkerFrame();
        Check(input.TakePress('I', true), "a fresh post-activity navigation tap remains available");
        settings = true; input.Capture(); Tap('J'); blocked = true;
        input.DiscardPresses(KeyboardInputContextReader.NavigationVirtualKeys, KeyboardInputContextReader.IsNormalFieldOwner);
        Check(input.TakeOldestPress(['J', 'L'], true) == 'J', "native navigation ownership does not steal active settings commands");
        settings = false; memory.Module = 3; memory.AllowedScope = 3; input.Capture(); Tap('I');
        input.DiscardPresses(KeyboardInputContextReader.NavigationVirtualKeys, KeyboardInputContextReader.IsNormalFieldOwner);
        Check(input.TakePress('I', true), "the inactive field cannot retire a world navigation command");
    }

    private static void QueuedCommandsKeepTheWorkerAwakeUntilDrained()
    {
        var down = new HashSet<int>();
        long time = 100;
        using var input = new BufferedKeyboardInput(() => true, down.Contains,
            () => 3L << 24, startObserver: false, monotonicClock: () => time, ticksPerSecond: 1000,
            keyGroups: KeyboardInputContextReader.OrderedKeys);
        void Tap(int key) { down.Add(key); time += 10; input.Capture(); down.Remove(key); time += 10; input.Capture(); }
        void Drain(int[] commands)
        {
            foreach (var command in commands)
            {
                input.BeginWorkerFrame();
                Check(input.TakePress(command, true), "ordered queued command survives the long monitor interval");
                if (!input.WaitForPressOrTimeout(0)) time += 3000;
            }
            Check(!input.WaitForPressOrTimeout(0), "drained commands do not spin the accessibility worker");
        }
        Tap('L'); Tap('L'); Tap('L');
        Drain(['L', 'L', 'L']);
        Tap('I'); Tap('L'); Tap('J');
        Drain(['I', 'L', 'J']);
        Tap('A');
        Check(input.WaitForPressOrTimeout(0) && !input.WaitForPressOrTimeout(0),
            "an unused alphabet key cannot keep waking the worker");
    }

    private static void PendingNavigationBypassesLongScanIntervalsWithoutConsumingInput()
    {
        var down = false;
        long time = 100;
        using var input = new BufferedKeyboardInput(() => true, key => key == 'L' && down,
            () => 3L << 24, startObserver: false, monotonicClock: () => time, ticksPerSecond: 1000,
            keyGroups: KeyboardInputContextReader.OrderedKeys);
        int[] navigationKeys = ['U', 'O', 'J', 'L', 'K', 'I', 'P'];
        down = true; time = 110; input.Capture(); down = false; time = 130; input.Capture();
        var scanIntervalMs = 3000;
        var scanElapsedMs = 35;
        var throttle = scanElapsedMs < scanIntervalMs && !input.HasPendingPresses(navigationKeys);
        Check(!throttle && input.HasPendingPresses(navigationKeys),
            "valid pending navigation bypasses a scan interval longer than the keyboard deadline without dequeueing it");
        input.BeginWorkerFrame();
        Check(input.TakePress('L', true) && !input.TakePress('L', true),
            "L is delivered once at its current native navigation owner");
    }

    private static void ObserverRetainsPressesWithoutAnAccessibilityWorkerPoll()
    {
        var physicalDown = 0;
        using var sawDown = new ManualResetEventSlim(false);
        using var sawUp = new ManualResetEventSlim(false);
        using var input = new BufferedKeyboardInput(() => true, key =>
        {
            if (key != 'K') return false;
            var pressed = Volatile.Read(ref physicalDown) != 0;
            if (pressed) sawDown.Set(); else sawUp.Set();
            return pressed;
        }, () => 3L);
        sawUp.Reset();
        Volatile.Write(ref physicalDown, 1);
        Check(sawDown.Wait(1000), "the independent observer samples while the accessibility worker is idle");
        sawUp.Reset(); Volatile.Write(ref physicalDown, 0);
        Check(sawUp.Wait(1000), "the independent observer samples the completed release");
        Check(input.TakePress('K', true) && !input.TakePress('K', true),
            "the real observer thread retains one completed press without a worker poll");
    }

    private static void NavigationAndBattleCommandsKeepCapturedOrder()
    {
        var down = new HashSet<int>();
        long owner = 3L << 24;
        using var input = new BufferedKeyboardInput(() => true, down.Contains, () => owner,
            startObserver: false, keyGroups: KeyboardInputContextReader.OrderedKeys);
        void Tap(int key) { down.Add(key); input.Capture(); down.Remove(key); input.Capture(); }
        Tap('I'); Tap('L'); input.BeginWorkerFrame();
        Check(!input.TakePress('L', true) && input.TakePress('I', true),
            "later L cannot run before an earlier I navigation command");
        Check(!input.TakePress('L', true), "one command owner executes one buffered command per worker frame");
        input.BeginWorkerFrame(); Check(input.TakePress('L', true), "the following L remains queued for navigation");
        Tap('L'); owner = 2L << 24; input.Capture();
        Check(!input.TakePress('L', true), "navigation L cannot become a battle Limit request across a module change");
        Tap('H'); Tap('L'); input.BeginWorkerFrame();
        Check(!input.TakePress('L', true) && input.TakePress('H', true), "HP before Limit retains battle command order");
        input.BeginWorkerFrame(); Check(input.TakePress('L', true), "battle receives its own subsequent L exactly once");
        owner = (1L << 24) | (341L << 1); input.Capture(); Tap('L');
        owner = (1L << 24) | (335L << 1); input.Capture(); input.BeginWorkerFrame();
        Check(!input.TakePress('L', true), "pending navigation L does not survive a field change within module 1");
    }

    private static void NativeContextUsesOnlyItsOwningDomain()
    {
        var memory = new HeaderMemory();
        var settings = false;
        var reader = new KeyboardInputContextReader(memory, () => (nint)10, () => settings);
        foreach (var module in new byte[] { 0, 2, 5 })
        {
            memory.Module = module; memory.AllowedScope = 0;
            Check(reader.Read() is not null, "title/menu/battle keys do not require inactive field or world data");
        }
        memory.Module = 1; memory.AllowedScope = 1;
        var first = reader.Read(); memory.Field = 335;
        Check(first is not null && reader.Read() != first, "field transition changes keyboard ownership");
        memory.Module = 3; memory.AllowedScope = 3;
        first = reader.Read(); memory.Map = 2;
        Check(first is not null && reader.Read() != first, "dive transition changes keyboard ownership");
        first = reader.Read(); settings = true;
        Check(reader.Read() != first, "settings ownership participates in the native context token");
        memory.ChangeModule = true;
        Check(reader.Read() is null, "a module transition during context capture is unavailable");
    }

    private sealed class HeaderMemory : ILegacyAddressSpace
    {
        internal byte Module;
        internal int AllowedScope;
        internal ushort Field = 341;
        internal int Map;
        internal bool ChangeModule;
        public bool TryRead(uint address, Span<byte> destination)
        {
            byte[] value;
            if (address == FieldPositionReader.AddressCurrentModule)
            {
                value = [Module];
                if (ChangeModule) Module++;
            }
            else if (address == FieldPositionReader.AddressFieldId && AllowedScope == 1)
                value = BitConverter.GetBytes(Field);
            else if (address == WorldMapStateReader.AddressWorldMapType && AllowedScope == 3)
                value = BitConverter.GetBytes(Map);
            else throw new InvalidOperationException("inactive native domain was queried");
            if (value.Length != destination.Length) return false;
            value.CopyTo(destination);
            return true;
        }
    }

    private static void Check(bool condition, string text)
    { if (!condition) throw new InvalidOperationException("Buffered keyboard: " + text); }
}
