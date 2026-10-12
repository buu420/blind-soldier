using System.Runtime.InteropServices;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Input;

/// <summary>
/// Architecture-neutral Win32 foreground/key sampling for the exact supported
/// Steam 2026 host. This adapter observes physical state only; it never
/// synthesizes or consumes game input.
/// </summary>
public sealed class Steam2026ForegroundInputAdapter : IDisposable
{
    private const int MaximumVirtualKey = 0xFF;

    private readonly ForegroundProcessGate foregroundGate;
    private readonly NavigationKeyPressTracker keyPressTracker = new();
    private readonly Func<int, short> getAsyncKeyState;
    private readonly Func<nint> getForegroundWindow;
    private BufferedKeyboardInput? keyboard;
    private bool settingsOwnsInput;
    private bool fieldNavigationSuppressed = true;
    private bool worldNavigationSuppressed = true;
    public bool ModSettingsOwnsInput
    {
        get => Volatile.Read(ref settingsOwnsInput);
        set { if (value != Volatile.Read(ref settingsOwnsInput)) { Volatile.Write(ref settingsOwnsInput, value); keyboard?.Invalidate(); } }
    }

    public bool IsKeyDown(int virtualKey) =>
        (getAsyncKeyState(virtualKey) & unchecked((short)0x8000)) != 0;

    public void BlockHeldModKeys()
    {
        for (var key = 1; key <= MaximumVirtualKey; key++)
            _ = keyPressTracker.Observe(key, IsKeyDown(key), false);
    }

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
        this.getForegroundWindow = getForegroundWindow;
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

    internal void EnableBufferedKeyboard(ILegacyAddressSpace memory, Action<string>? log, Func<bool> sessionActive,
        bool startObserver = true)
    {
        if (keyboard is not null) return;
        var context = new KeyboardInputContextReader(memory, getForegroundWindow, () => ModSettingsOwnsInput,
            () => Volatile.Read(ref fieldNavigationSuppressed), () => Volatile.Read(ref worldNavigationSuppressed));
        keyboard = new BufferedKeyboardInput(() => sessionActive() && IsCurrentProcessForeground(),
            IsKeyDown, context.Read, log, startObserver: startObserver, keyGroups: KeyboardInputContextReader.OrderedKeys);
        log?.Invoke("Keyboard accessibility input uses an independent 8 ms foreground observer; native game input is unchanged.");
    }

    internal void CaptureKeyboardSampleForTest(Func<long?> context, Func<bool>? sessionActive = null)
    {
        keyboard ??= new BufferedKeyboardInput(() => (sessionActive?.Invoke() ?? true) && IsCurrentProcessForeground(), IsKeyDown,
            () => context() is { } value ? value * 2 + (ModSettingsOwnsInput ? 1 : 0) : null,
            startObserver: false);
        keyboard.Capture();
    }

    internal void BeginWorkerFrame() => keyboard?.BeginWorkerFrame();
    internal void InvalidateBufferedKeyboard() => keyboard?.Invalidate();
    internal bool SetFieldNavigationSuppressed(bool suppressed)
    {
        var changed = suppressed != Volatile.Read(ref fieldNavigationSuppressed);
        Volatile.Write(ref fieldNavigationSuppressed, suppressed);
        if (suppressed) keyboard?.DiscardPresses(KeyboardInputContextReader.NavigationVirtualKeys,
            KeyboardInputContextReader.IsNormalFieldOwner);
        return changed && keyboard is not null;
    }

    internal bool ObserveFieldNavigationRisingEdge(int virtualKey)
    {
        if (keyboard is not null && Volatile.Read(ref fieldNavigationSuppressed))
        {
            keyboard.DiscardPresses(KeyboardInputContextReader.NavigationVirtualKeys,
                KeyboardInputContextReader.IsNormalFieldOwner);
            return false;
        }
        return ObserveRisingEdge(virtualKey);
    }

    internal bool SetWorldNavigationSuppressed(bool suppressed)
    {
        var changed = suppressed != Volatile.Read(ref worldNavigationSuppressed);
        Volatile.Write(ref worldNavigationSuppressed, suppressed);
        if (suppressed) keyboard?.DiscardPresses(KeyboardInputContextReader.NavigationVirtualKeys,
            KeyboardInputContextReader.IsNormalWorldOwner);
        return changed && keyboard is not null;
    }

    internal bool ObserveWorldNavigationRisingEdge(int virtualKey)
    {
        if (keyboard is not null && Volatile.Read(ref worldNavigationSuppressed))
        {
            keyboard.DiscardPresses(KeyboardInputContextReader.NavigationVirtualKeys,
                KeyboardInputContextReader.IsNormalWorldOwner);
            return false;
        }
        return ObserveRisingEdge(virtualKey);
    }

    internal int ObserveSettingsKeyGroup(IReadOnlyList<int> keys)
    {
        if (keyboard is not null) return keyboard.TakeOldestPress(keys, IsCurrentProcessForeground());
        foreach (var key in keys) if (ObserveRisingEdgeCore(key, true)) return key;
        return 0;
    }

    public bool ObserveRisingEdge(int virtualKey) => ObserveRisingEdgeCore(virtualKey, false);
    internal bool ObservePhysicalGameActionEdge(int virtualKey) => ObserveRisingEdgeCore(virtualKey, false, useBuffer: false);
    internal bool ObserveSubmarineRisingEdge(int virtualKey) => KeyboardInputContextReader.IsSubmarineActionKey(virtualKey)
        ? ObservePhysicalGameActionEdge(virtualKey) : ObserveRisingEdge(virtualKey);

    private bool ObserveRisingEdgeCore(int virtualKey, bool settings, bool useBuffer = true)
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
        var allowed = foregroundBefore && foregroundAfter && (settings || !ModSettingsOwnsInput);
        if (!settings && ModSettingsOwnsInput) return false;
        if (!useBuffer && keyboard is not null && BufferedKeyboardInput.Tracks(virtualKey))
            return keyboard.ObservePhysicalAction(virtualKey, allowed);
        if (useBuffer && keyboard is not null && BufferedKeyboardInput.Tracks(virtualKey))
            return keyboard.TakePress(virtualKey, allowed);
        return keyPressTracker.Observe(
            virtualKey,
            isDown,
            allowed);
    }

    public void Dispose() => keyboard?.Dispose();

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
