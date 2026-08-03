using System.Runtime.InteropServices;
using Ff7.Accessibility.Core;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Input;

/// <summary>
/// Architecture-neutral Win32 foreground/key sampling for the exact supported
/// Steam 2026 host. This adapter observes physical state only; it never
/// synthesizes or consumes game input.
/// </summary>
public sealed class Steam2026ForegroundInputAdapter
{
    private const int MaximumVirtualKey = 0xFF;

    private readonly ForegroundProcessGate foregroundGate;
    private readonly NavigationKeyPressTracker keyPressTracker = new();
    private readonly Func<int, short> getAsyncKeyState;

    internal Steam2026ForegroundInputAdapter(
        Func<nint> getForegroundWindow,
        Func<nint, uint> getWindowProcessId,
        Func<int, short> getAsyncKeyState,
        uint currentProcessId)
    {
        foregroundGate = new ForegroundProcessGate(
            getForegroundWindow ?? throw new ArgumentNullException(nameof(getForegroundWindow)),
            getWindowProcessId ?? throw new ArgumentNullException(nameof(getWindowProcessId)),
            currentProcessId);
        this.getAsyncKeyState = getAsyncKeyState
            ?? throw new ArgumentNullException(nameof(getAsyncKeyState));
    }

    public static Steam2026ForegroundInputAdapter CreateCurrentProcess(
        Steam2026FingerprintResult fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        if (!fingerprint.IsSupported
            || !fingerprint.Identity.Is64Bit
            || !string.Equals(
                fingerprint.Identity.RuntimeId,
                Steam2026Fingerprint.SupportedRuntimeId,
                StringComparison.Ordinal)
            || !string.Equals(
                fingerprint.Identity.Sha256,
                Steam2026Fingerprint.SupportedSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Foreground input is available only for the exact supported Steam 2026 executable.");
        }

        return new Steam2026ForegroundInputAdapter(
            NativeMethods.GetForegroundWindow,
            NativeMethods.GetWindowProcessId,
            NativeMethods.GetAsyncKeyState,
            checked((uint)Environment.ProcessId));
    }

    public bool IsCurrentProcessForeground() =>
        foregroundGate.IsCurrentProcessForeground();

    public bool ObserveRisingEdge(int virtualKey)
    {
        if (virtualKey is <= 0 or > MaximumVirtualKey)
        {
            throw new ArgumentOutOfRangeException(nameof(virtualKey));
        }

        // Sample ownership on both sides of the physical key read. A focus
        // transition during the sample updates held-key state but cannot emit.
        var foregroundBefore = foregroundGate.IsCurrentProcessForeground();
        var isDown = (getAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
        var foregroundAfter = foregroundGate.IsCurrentProcessForeground();
        return keyPressTracker.Observe(
            virtualKey,
            isDown,
            foregroundBefore && foregroundAfter);
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern nint GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint GetWindowThreadProcessId(
            nint window,
            out uint processId);

        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern short GetAsyncKeyState(int virtualKey);

        internal static uint GetWindowProcessId(nint window)
        {
            _ = GetWindowThreadProcessId(window, out var processId);
            return processId;
        }
    }
}
