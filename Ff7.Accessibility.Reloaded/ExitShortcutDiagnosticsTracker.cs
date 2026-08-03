namespace Ff7.Accessibility.Reloaded;

public readonly record struct ExitShortcutDiagnostic(
    int PreviousModule,
    int CurrentModule,
    bool ControlQRecent,
    bool ControlActive,
    bool QActive,
    bool WasForeground);

public sealed class ExitShortcutDiagnosticsTracker
{
    public const int ExitModule = 19;

    private readonly TimeSpan recentWindow;
    private int? previousModule;
    private DateTime lastForegroundControlQAt = DateTime.MinValue;
    private bool controlActive;
    private bool qActive;
    private bool wasForeground;

    public ExitShortcutDiagnosticsTracker(TimeSpan recentWindow)
    {
        this.recentWindow = recentWindow > TimeSpan.Zero
            ? recentWindow
            : throw new ArgumentOutOfRangeException(nameof(recentWindow));
    }

    public void ObserveInput(DateTime now, bool controlActive, bool qActive, bool isForeground)
    {
        this.controlActive = controlActive;
        this.qActive = qActive;
        wasForeground = isForeground;
        if (isForeground && controlActive && qActive)
        {
            lastForegroundControlQAt = now;
        }
    }

    public ExitShortcutDiagnostic? ObserveModule(int currentModule, DateTime now)
    {
        var oldModule = previousModule;
        previousModule = currentModule;
        if (oldModule is null || oldModule.Value == currentModule || currentModule != ExitModule)
        {
            return null;
        }

        var controlQRecent = lastForegroundControlQAt != DateTime.MinValue &&
            now >= lastForegroundControlQAt &&
            now - lastForegroundControlQAt <= recentWindow;
        return new ExitShortcutDiagnostic(
            oldModule.Value,
            currentModule,
            controlQRecent,
            controlActive,
            qActive,
            wasForeground);
    }
}
