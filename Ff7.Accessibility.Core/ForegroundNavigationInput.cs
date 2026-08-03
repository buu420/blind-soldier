namespace Ff7.Accessibility.Core;

/// <summary>
/// Architecture-neutral foreground ownership policy. The platform adapter
/// supplies window/process queries; Core decides whether accessibility input
/// is allowed to act.
/// </summary>
public sealed class ForegroundProcessGate
{
    private readonly Func<nint> getForegroundWindow;
    private readonly Func<nint, uint> getWindowProcessId;
    private readonly uint currentProcessId;

    public ForegroundProcessGate(
        Func<nint> getForegroundWindow,
        Func<nint, uint> getWindowProcessId,
        uint currentProcessId)
    {
        this.getForegroundWindow = getForegroundWindow
            ?? throw new ArgumentNullException(nameof(getForegroundWindow));
        this.getWindowProcessId = getWindowProcessId
            ?? throw new ArgumentNullException(nameof(getWindowProcessId));
        this.currentProcessId = currentProcessId != 0
            ? currentProcessId
            : throw new ArgumentOutOfRangeException(nameof(currentProcessId));
    }

    public bool IsCurrentProcessForeground()
    {
        var window = getForegroundWindow();
        return window != 0 && getWindowProcessId(window) == currentProcessId;
    }
}

/// <summary>
/// Tracks physical key state even while backgrounded, but emits a rising edge
/// only when the owning game process is foreground. A key held before focus is
/// restored therefore cannot trigger an accessibility command.
/// </summary>
public sealed class NavigationKeyPressTracker
{
    private readonly Dictionary<int, bool> keyStates = new();

    public bool Observe(int virtualKey, bool isDown, bool isForeground)
    {
        keyStates.TryGetValue(virtualKey, out var wasDown);
        keyStates[virtualKey] = isDown;
        return isForeground && isDown && !wasDown;
    }
}
