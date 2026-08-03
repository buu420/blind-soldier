using Ff7.Accessibility.Steam2026X64;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;

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
