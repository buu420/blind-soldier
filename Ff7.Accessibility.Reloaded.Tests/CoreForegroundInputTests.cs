namespace Ff7.Accessibility.Reloaded.Tests;

internal static class CoreForegroundInputTests
{
    public static void Run()
    {
        if (typeof(Ff7.Accessibility.Core.ForegroundProcessGate).Assembly !=
            typeof(Ff7.Accessibility.Core.AccessibilityRuntime).Assembly)
        {
            throw new InvalidOperationException("Foreground ownership policy must live in the shared Core assembly.");
        }

        nint foregroundWindow = 0;
        uint foregroundProcess = 0;
        var gate = new Ff7.Accessibility.Core.ForegroundProcessGate(
            () => foregroundWindow,
            _ => foregroundProcess,
            currentProcessId: 42);
        AssertEqual(false, gate.IsCurrentProcessForeground(), "zero foreground window");
        foregroundWindow = 10;
        foregroundProcess = 7;
        AssertEqual(false, gate.IsCurrentProcessForeground(), "foreign foreground process");
        foregroundProcess = 42;
        AssertEqual(true, gate.IsCurrentProcessForeground(), "matching foreground process");

        var tracker = new Ff7.Accessibility.Core.NavigationKeyPressTracker();
        AssertEqual(false, tracker.Observe(0x49, true, isForeground: false), "background key-down must not fire");
        AssertEqual(false, tracker.Observe(0x49, true, isForeground: true), "background-held key must not fire on focus return");
        AssertEqual(false, tracker.Observe(0x49, false, isForeground: true), "key release");
        AssertEqual(true, tracker.Observe(0x49, true, isForeground: true), "foreground rising edge");
        AssertEqual(false, tracker.Observe(0x49, true, isForeground: true), "held foreground key deduplicated");
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}.");
        }
    }
}
