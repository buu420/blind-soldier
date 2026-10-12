using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class Steam2026ForegroundInputTests
{
    public static void Run(
        Steam2026FingerprintResult supported,
        Steam2026FingerprintResult unsupported)
    {
        _ = Steam2026ForegroundInputAdapter.CreateCurrentProcess(supported);
        RejectsUnsupportedExecutable(unsupported);
        EmitsOnlyForegroundRisingEdges();
        RejectsForegroundOwnershipRaces();
        RunBufferedInputTests();
    }

    internal static void RunBufferedInputTests()
    {
        var foreground = true;
        var down = false;
        var active = true;
        long? owner = 3;
        var input = new Steam2026ForegroundInputAdapter(() => (nint)10,
            _ => foreground ? 77u : 88u,
            key => key == 'K' && down ? unchecked((short)0x8000) : (short)0, 77);
        void Sample()
        {
            input.CaptureKeyboardSampleForTest(() => owner, () => active);
        }
        Sample();
        Equal(false, input.ObserveRisingEdge('K'), "initial released key is silent");
        down = true; Sample(); // press at 10 ms, before the worker's next 35 ms poll
        down = false; Sample(); // release at 30 ms
        Equal(true, input.ObserveRisingEdge('K'), "completed keyboard tap survives a slow worker iteration");
        Equal(false, input.ObserveRisingEdge('K'), "one captured tap is consumed once");
        down = true; Sample(); down = false; Sample();
        down = true; Sample(); down = false; Sample();
        Equal(true, input.ObserveRisingEdge('K'), "first of two completed taps is retained");
        Equal(true, input.ObserveRisingEdge('K'), "second separate tap is retained");
        Equal(false, input.ObserveRisingEdge('K'), "two taps never become a third command");
        down = true; Sample();
        Equal(true, input.ObserveRisingEdge('K'), "new held press begins once");
        Sample();
        Equal(false, input.ObserveRisingEdge('K'), "held physical key cannot replay after its buffered edge");
        down = false; Sample(); down = true; Sample();
        foreground = false; Sample(); foreground = true; Sample();
        Equal(false, input.ObserveRisingEdge('K'), "focus loss retires pending keys and suppresses held keys on return");
        down = false; Sample(); down = true; Sample();
        owner = 1; Sample(); owner = 3; Sample();
        Equal(false, input.ObserveRisingEdge('K'), "owner A to B to A cannot replay an earlier press");
        down = false; Sample(); down = true; Sample();
        owner = null; Sample(); owner = 3; Sample();
        Equal(false, input.ObserveRisingEdge('K'), "unreadable native context retires pending input and rebaselines holds");
        down = false; Sample(); down = true; Sample();
        input.ModSettingsOwnsInput = true; Sample(); input.ModSettingsOwnsInput = false; Sample();
        Equal(false, input.ObserveRisingEdge('K'), "settings ownership round trip cannot replay old navigation input");
        active = false; input.InvalidateBufferedKeyboard(); down = false; Sample();
        down = true; Sample(); down = false; Sample();
        input.InvalidateBufferedKeyboard(); active = true; Sample();
        Equal(false, input.ObserveRisingEdge('K'), "a completed key typed during suspension cannot replay on resume");
        input.ModSettingsOwnsInput = true; down = false; Sample();
        down = true; Sample(); down = false; Sample();
        down = true; Sample(); down = false; Sample();
        input.BeginWorkerFrame();
        Equal((int)'K', input.ObserveSettingsKeyGroup(new[] { (int)'K' }), "settings consumes its first buffered K");
        Equal(false, input.ObserveRisingEdge('K'), "inactive navigation does not consume settings input");
        input.BeginWorkerFrame();
        Equal((int)'K', input.ObserveSettingsKeyGroup(new[] { (int)'K' }), "second settings K survives inactive navigation polling");
        input.Dispose();
        BufferedNativeFieldOwnershipRejectsNormalNavigation();
        Console.WriteLine("buffered keyboard tap, hold, focus and owner checks passed.");
    }

    private static void BufferedNativeFieldOwnershipRejectsNormalNavigation()
    {
        var down = new HashSet<int>();
        using var input = new Steam2026ForegroundInputAdapter(() => (nint)10, _ => 77,
            key => down.Contains(key) ? unchecked((short)0x8000) : (short)0, 77);
        var memory = new FieldHeaderMemory();
        input.EnableBufferedKeyboard(memory, null, () => true, startObserver: false);
        void Sample() => input.CaptureKeyboardSampleForTest(() => 0);
        void Tap(int key) { down.Add(key); Sample(); down.Remove(key); Sample(); }
        input.SetFieldNavigationSuppressed(false); Sample();
        Tap('I'); Tap('L'); Tap('J');
        input.SetFieldNavigationSuppressed(true); Sample(); Tap('P');
        Equal(false, input.ObserveFieldNavigationRisingEdge('P'), "busy field cannot run a pre-scan auto-walk toggle");
        Tap('K'); Equal(true, input.ObserveRisingEdge('K'), "generic native activity status remains available");
        Tap('R'); Equal(true, input.ObserveRisingEdge('R'), "repeat remains available while field navigation is busy");
        Tap(0x7A); Equal(0x7A, input.ObserveSettingsKeyGroup([0x7A]), "F11 remains available while field navigation is busy");
        input.SetFieldNavigationSuppressed(false);
        input.BeginWorkerFrame();
        Equal(false, input.ObserveFieldNavigationRisingEdge('I'), "activity backlog cannot replay after recovery");
        Sample(); Tap('I'); input.BeginWorkerFrame();
        Equal(true, input.ObserveFieldNavigationRisingEdge('I'), "fresh navigation resumes after a coherent recovery");
        memory.Module = 3; Sample(); input.SetWorldNavigationSuppressed(false); Sample();
        Tap('I'); Tap('L'); Tap('J'); input.SetWorldNavigationSuppressed(true); Sample(); Tap('P');
        Equal(false, input.ObserveWorldNavigationRisingEdge('P'), "native world ownership blocks pre-scan travel toggles");
        input.SetWorldNavigationSuppressed(false); input.BeginWorkerFrame();
        Equal(false, input.ObserveWorldNavigationRisingEdge('I'), "same-map world backlog cannot replay after native recovery");
        Sample(); Tap('I'); input.BeginWorkerFrame();
        Equal(true, input.ObserveWorldNavigationRisingEdge('I'), "new world travel remains available after native recovery");
        input.ModSettingsOwnsInput = true; Sample(); Tap('J'); input.SetWorldNavigationSuppressed(true);
        Equal(false, input.ObserveWorldNavigationRisingEdge('J'), "inactive world navigation cannot steal a settings command");
        input.BeginWorkerFrame();
        Equal((int)'J', input.ObserveSettingsKeyGroup(['J', 'L']), "world suppression preserves active settings commands");
        input.ModSettingsOwnsInput = false; memory.Module = 10; Sample();
        Tap('I'); Tap('I');
        Equal(false, input.ObserveSubmarineRisingEdge('I'), "completed paused pursuit presses are not banked as game actions");
        Equal(false, input.ObserveSubmarineRisingEdge('I'), "a second paused pursuit cannot replay after recovery");
        down.Add('O'); Sample();
        Equal(true, input.ObserveSubmarineRisingEdge('O'), "physical submarine stop remains immediately available");
        Equal(false, input.ObserveSubmarineRisingEdge('O'), "held submarine stop remains one physical edge");
        down.Remove('O'); Sample(); input.ObserveSubmarineRisingEdge('O');
        Tap('K'); Equal(true, input.ObserveSubmarineRisingEdge('K'), "submarine status retains buffered feedback");
        memory.Module = 9; Sample(); Tap('I'); Tap('L');
        Equal(false, input.ObservePhysicalGameActionEdge('L'), "completed Condor selection cannot be dispatched ahead of an earlier action");
        Equal(false, input.ObservePhysicalGameActionEdge('I'), "Condor actions retain the existing physical-edge phase policy");
        memory.Module = 1; input.SetFieldNavigationSuppressed(false); Sample();
        down.Add('I'); Sample(); input.BeginWorkerFrame();
        Equal(true, input.ObserveFieldNavigationRisingEdge('I'), "buffered field I begins a held navigation press");
        memory.Module = 10;
        Equal(false, input.ObserveSubmarineRisingEdge('I'), "held field I cannot become a new submarine pursuit edge");
        down.Remove('I'); Sample(); input.ObserveSubmarineRisingEdge('I');
        down.Add('I'); Sample();
        Equal(true, input.ObserveSubmarineRisingEdge('I'), "fresh submarine I works after release");
        memory.Module = 1; Sample(); down.Remove('I'); Sample(); memory.Module = 9; Sample();
        down.Add('I'); Sample();
        Equal(true, input.ObservePhysicalGameActionEdge('I'), "released field interval rearms a fresh returning Condor action");
        memory.Module = 10;
        Equal(false, input.ObserveSubmarineRisingEdge('I'), "held Condor action cannot arm submarine pursuit across ownership");
    }

    private sealed class FieldHeaderMemory : ILegacyAddressSpace
    {
        internal byte Module = 1;
        public bool TryRead(uint address, Span<byte> destination)
        {
            byte[] bytes = address == FieldPositionReader.AddressCurrentModule ? [Module] :
                address == FieldPositionReader.AddressFieldId ? BitConverter.GetBytes((ushort)341) :
                address == WorldMapStateReader.AddressWorldMapType ? BitConverter.GetBytes(2) : [];
            if (bytes.Length != destination.Length) return false;
            bytes.CopyTo(destination); return true;
        }
    }

    private static void RejectsUnsupportedExecutable(Steam2026FingerprintResult unsupported)
    {
        var threw = false;
        try
        {
            _ = Steam2026ForegroundInputAdapter.CreateCurrentProcess(unsupported);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Equal(true, threw, "foreground input adapter rejects an unsupported executable");
    }

    private static void EmitsOnlyForegroundRisingEdges()
    {
        const uint processId = 77;
        var foregroundWindow = (nint)10;
        var windowProcessId = processId;
        var keyDown = false;
        var adapter = new Steam2026ForegroundInputAdapter(
            () => foregroundWindow,
            _ => windowProcessId,
            _ => keyDown ? unchecked((short)0x8000) : (short)0,
            processId);

        Equal(true, adapter.IsCurrentProcessForeground(), "owning x64 game window is foreground");
        Equal(false, adapter.ObserveRisingEdge(0x4E), "released foreground key is silent");
        keyDown = true;
        Equal(true, adapter.ObserveRisingEdge(0x4E), "foreground physical key rising edge");
        Equal(false, adapter.ObserveRisingEdge(0x4E), "held foreground key is deduplicated");

        windowProcessId = 88;
        keyDown = false;
        Equal(false, adapter.ObserveRisingEdge(0x4E), "background key release updates state silently");
        keyDown = true;
        Equal(false, adapter.ObserveRisingEdge(0x4E), "background key press is silent");
        windowProcessId = processId;
        Equal(false, adapter.ObserveRisingEdge(0x4E), "key held before focus return cannot fire");
        keyDown = false;
        Equal(false, adapter.ObserveRisingEdge(0x4E), "foreground release remains silent");
        keyDown = true;
        Equal(true, adapter.ObserveRisingEdge(0x4E), "new foreground press fires after release");

        foregroundWindow = 0;
        Equal(false, adapter.IsCurrentProcessForeground(), "null foreground window is not owned");
    }

    private static void RejectsForegroundOwnershipRaces()
    {
        const uint processId = 91;
        var calls = 0;
        var adapter = new Steam2026ForegroundInputAdapter(
            () => ++calls == 1 ? (nint)20 : (nint)21,
            window => window == (nint)20 ? processId : 92u,
            _ => unchecked((short)0x8000),
            processId);

        Equal(false, adapter.ObserveRisingEdge(0x51), "foreground ownership change during sampling fails closed");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
