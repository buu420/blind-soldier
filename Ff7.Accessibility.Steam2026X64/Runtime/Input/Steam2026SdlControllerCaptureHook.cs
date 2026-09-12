using System.Runtime.InteropServices;
using Ff7.Accessibility.Core;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Input;

/// <summary>
/// Takes the navigation menu's buttons out of the controller state the Steam 2026 host
/// is given, in the same read that produced them.
///
/// <para>The seam is SDL, on binary evidence: the exact supported <c>FFVII.exe</c>
/// imports <c>SDL_GameControllerGetButton</c>, <c>SDL_GameControllerUpdate</c>,
/// <c>SDL_GameControllerGetAttached</c> and <c>SDL_GameControllerClose</c> from the
/// <c>SDL2.dll</c> beside it. Everything used is the published API - the cdecl
/// signature <c>Uint8 SDL_GameControllerGetButton(SDL_GameController*,
/// SDL_GameControllerButton)</c> and the documented button numbering - so nothing
/// rests on the host's translated code, which is encrypted on disk.</para>
///
/// <para>Identity is the controller pointer the game supplies, latched. A second pad,
/// or the same pad after a reconnect, is a different device: its edges must not be
/// read against the first one's held state, and its reads must not be filtered by the
/// first one's decision. The pointer is never dereferenced outside the call that
/// supplied it, and <c>SDL_GameControllerClose</c> is hooked so the latch is dropped
/// the moment SDL frees it.</para>
/// </summary>
internal sealed class Steam2026SdlControllerCaptureHook : IDisposable
{
    private const string SdlModule = "SDL2.dll";

    /// <summary>SDL's own button numbers, from the published enum.</summary>
    private static readonly (int SdlButton, GamepadButton Button)[] ButtonMap =
    [
        (0, GamepadButton.A),
        (1, GamepadButton.B),
        (2, GamepadButton.X),
        (3, GamepadButton.Y),
        (7, GamepadButton.LeftThumb),
        (8, GamepadButton.RightThumb),
        (9, GamepadButton.LeftShoulder),
        (10, GamepadButton.RightShoulder),
        (11, GamepadButton.DPadUp),
        (12, GamepadButton.DPadDown),
        (13, GamepadButton.DPadLeft),
        (14, GamepadButton.DPadRight),
    ];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte GetButtonDelegate(nint controller, int button);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void CloseDelegate(nint controller);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int GetAttachedDelegate(nint controller);

    // Detours are rooted here for the life of the hook, including ones the backend
    // failed to remove: a collected delegate is a jump into freed memory on the
    // game's input path.
    private readonly GetButtonDelegate getButtonDetour;
    private readonly CloseDelegate? closeDetour;
    private readonly GetAttachedDelegate? getAttached;

    /// <summary>Injected under test; null in production, where the trampoline is used.</summary>
    private readonly GetButtonDelegate? originalGetButton;
    private readonly ControllerNavigationCapture capture;
    private readonly Func<DateTime> now;
    private readonly Action<string>? log;
    private readonly IHook<GetButtonDelegate>? getButtonHook;
    private readonly IHook<CloseDelegate>? closeHook;
    private readonly object deviceSync = new();

    [ThreadStatic]
    private static bool inSnapshot;

    private nint latchedController;
    private int disposed;

    private Steam2026SdlControllerCaptureHook(
        IReloadedHooks hooks,
        SdlExports exports,
        Func<Steam2026SdlControllerCaptureHook, ControllerNavigationCapture> createCapture,
        Func<DateTime>? now,
        Action<string>? log)
    {
        this.now = now ?? (static () => DateTime.UtcNow);
        this.log = log;
        capture = createCapture(this);
        getButtonDetour = OnGetButton;

        // The whole cohort is created before any of it is activated, and a failure
        // rolls the activated ones back. Half a cohort is live detours calling into an
        // object whose construction threw.
        try
        {
            getButtonHook = hooks.CreateHook(getButtonDetour, (long)exports.GetButton);
            if (exports.Close != 0)
            {
                closeDetour = OnClose;
                closeHook = hooks.CreateHook(closeDetour, (long)exports.Close);
            }

            getButtonHook!.Activate();
            closeHook?.Activate();
        }
        catch
        {
            DisableHooks();
            throw;
        }

        if (exports.GetAttached != 0)
        {
            getAttached = Marshal.GetDelegateForFunctionPointer<GetAttachedDelegate>(exports.GetAttached);
        }
    }

    internal ControllerNavigationCapture Capture => capture;

    private byte CallOriginalGetButton(nint controller, int button) =>
        originalGetButton is not null
            ? originalGetButton(controller, button)
            : getButtonHook!.OriginalFunction(controller, button);

    /// <summary>
    /// Builds the decide path over an injected getter and clock, with no hooking
    /// backend. The live close-on-open fault is a timing fault, so it needs a clock
    /// the test controls; the isolated native host proves the same code against real
    /// SDL but cannot hold time still.
    /// </summary>
    internal static Steam2026SdlControllerCaptureHook CreateForDecideTest(
        Func<Func<bool>, ControllerNavigationCapture> createCapture,
        GetButtonDelegate originalGetButton,
        GetAttachedDelegate getAttached,
        Func<DateTime> now,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(createCapture);
        ArgumentNullException.ThrowIfNull(originalGetButton);
        ArgumentNullException.ThrowIfNull(getAttached);
        ArgumentNullException.ThrowIfNull(now);
        return new Steam2026SdlControllerCaptureHook(createCapture, originalGetButton, getAttached, now, log);
    }

    private Steam2026SdlControllerCaptureHook(
        Func<Func<bool>, ControllerNavigationCapture> createCapture,
        GetButtonDelegate originalGetButton,
        GetAttachedDelegate getAttached,
        Func<DateTime> now,
        Action<string>? log)
    {
        this.now = now;
        this.log = log;
        this.originalGetButton = originalGetButton;
        this.getAttached = getAttached;
        Steam2026SdlControllerCaptureHook? self = null;
        capture = createCapture(() => self?.IsInstalled == true);
        self = this;
        getButtonDetour = OnGetButton;
    }

    /// <summary>One game button read, as SDL would deliver it. Test seam.</summary>
    internal byte InvokeGetButtonForTest(nint controller, int button) =>
        OnGetButton(controller, button);

    internal bool IsInstalled =>
        Volatile.Read(ref disposed) == 0 &&
        (getButtonHook is { IsHookActivated: true, IsHookEnabled: true }
            || (getButtonHook is null && originalGetButton is not null));

    /// <summary>The device the menu is listening to, or zero. Diagnostic only.</summary>
    internal nint LatchedController
    {
        get
        {
            lock (deviceSync)
            {
                return latchedController;
            }
        }
    }

    internal static bool TryInstall(
        IReloadedHooks hooks,
        Func<Func<bool>, ControllerNavigationCapture> createCapture,
        out Steam2026SdlControllerCaptureHook installed,
        out string diagnostic,
        Func<DateTime>? now = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(createCapture);
        installed = null!;

        if (!TryResolveExports(out var exports, out diagnostic))
        {
            return false;
        }

        try
        {
            Steam2026SdlControllerCaptureHook? candidate = null;
            candidate = new Steam2026SdlControllerCaptureHook(
                hooks,
                exports,
                _ => createCapture(() => candidate?.IsInstalled == true),
                now,
                log);
            installed = candidate;
            diagnostic = "Controller navigation capture installed over SDL2!SDL_GameControllerGetButton.";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"SDL_GameControllerGetButton could not be hooked: {ex.Message}";
            return false;
        }
    }

    internal static bool TryResolveExports(out SdlExports exports, out string diagnostic)
    {
        exports = default;
        var module = NativeMethods.GetModuleHandle(SdlModule);
        if (module == 0)
        {
            // Normal early on: SDL is loaded when the host first looks for a pad,
            // which can be well after we attach. The caller retries.
            diagnostic = "SDL2.dll is not loaded yet; controller navigation will retry.";
            return false;
        }

        var getButton = NativeMethods.GetProcAddress(module, "SDL_GameControllerGetButton");
        if (getButton == 0)
        {
            diagnostic = "SDL2.dll does not export SDL_GameControllerGetButton.";
            return false;
        }

        exports = new SdlExports(
            getButton,
            NativeMethods.GetProcAddress(module, "SDL_GameControllerUpdate"),
            NativeMethods.GetProcAddress(module, "SDL_GameControllerClose"),
            NativeMethods.GetProcAddress(module, "SDL_GameControllerGetAttached"));
        diagnostic = "SDL2.dll controller exports resolved.";
        return true;
    }

    /// <summary>
    /// SDL freeing a controller. Anything latched to it stops being ours immediately:
    /// the pointer can be reused for a different device, and filtering that device by
    /// this one's decision would take a second player's buttons away.
    /// </summary>
    private void OnClose(nint controller)
    {
        try
        {
            bool wasOurs;
            lock (deviceSync)
            {
                wasOurs = controller != 0 && controller == latchedController;
                if (wasOurs)
                {
                    ForgetDeviceLocked();
                }
            }

            if (wasOurs)
            {
                // Told as a disconnect, so the menu closes and everything held is
                // rearmed rather than surviving into the next device.
                _ = capture.ObserveRawPoll(GamepadSnapshot.Disconnected, now());
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Controller navigation could not release a closed device: {ex.Message}");
        }
        finally
        {
            closeHook!.OriginalFunction(controller);
        }
    }

    private byte OnGetButton(nint controller, int button)
    {
        var raw = CallOriginalGetButton(controller, button);
        if (Volatile.Read(ref disposed) != 0 || inSnapshot)
        {
            // Either we are shutting down, or this is one of our own snapshot reads
            // coming back round. Neither may run the policy again.
            return raw;
        }

        try
        {
            var mapped = MapButton(button);
            if (!TryOwn(controller))
            {
                return raw;
            }

            var strip = Decide(controller);
            return mapped != GamepadButton.None && (strip & mapped) == mapped ? (byte)0 : raw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Controller navigation capture failed and was bypassed: {ex.Message}");
            return raw;
        }
    }

    /// <summary>
    /// Whether this device is the one the menu is listening to, latching it if none is.
    /// A device we have not latched is read and filtered by nobody.
    /// </summary>
    private bool TryOwn(nint controller)
    {
        if (controller == 0)
        {
            return false;
        }

        lock (deviceSync)
        {
            if (latchedController == controller)
            {
                return true;
            }

            if (latchedController != 0)
            {
                return false;
            }

            ForgetDeviceLocked();
            latchedController = controller;
        }

        // A newly latched device starts neutral: whatever is already held on it has to
        // be released before it can mean anything.
        _ = capture.ObserveRawPoll(GamepadSnapshot.Disconnected, now());
        return true;
    }

    /// <summary>
    /// Reads the whole owned-button word through the original getter on every call
    /// the game makes, and runs the policy on it.
    ///
    /// <para>Anything cheaper was wrong. Tracking only the bit the game asked about
    /// misses R3 entirely if the game never queries R3; sweeping only on
    /// <c>SDL_GameControllerUpdate</c> misses it if the game refreshes through
    /// <c>SDL_JoystickUpdate</c> or <c>SDL_PumpEvents</c> instead; and a time window
    /// misses a press and release inside it. Identical states produce no duplicate
    /// edges, so reading every time costs a dozen array lookups and removes every one
    /// of those holes.</para>
    /// </summary>
    private GamepadButton Decide(nint controller)
    {
        var buttons = ReadSnapshot(controller, out var attached);
        if (!attached)
        {
            bool wasOurs;
            lock (deviceSync)
            {
                wasOurs = latchedController == controller;
                if (wasOurs)
                {
                    ForgetDeviceLocked();
                }
            }

            if (wasOurs)
            {
                _ = capture.ObserveRawPoll(GamepadSnapshot.Disconnected, now());
            }

            return GamepadButton.None;
        }

        lock (deviceSync)
        {
            if (latchedController != controller)
            {
                return GamepadButton.None;
            }
        }

        // Every poll reaches the policy, including one whose buttons are identical to
        // the last. It used to return a cached mask here instead, and that froze
        // everything the policy measures in time: the watchdog saw no poll and retired
        // the menu about a second after the player stopped moving a button, D-pad
        // repeats never came due, and a worker close request was never picked up.
        // Identical states produce no duplicate edges - the edge tracker already
        // dedupes - so running it every time is both safe and the only way the clock
        // advances for it.
        var strip = capture.ObserveRawPoll(new GamepadSnapshot(true, 0, 0, buttons), now());

        lock (deviceSync)
        {
            return latchedController == controller ? strip : GamepadButton.None;
        }
    }

    private void ForgetDeviceLocked()
    {
        latchedController = 0;
    }

    /// <summary>
    /// Builds the whole button word from the original getter through the pointer the
    /// game has just supplied, and lets go of it again when this returns.
    /// </summary>
    private GamepadButton ReadSnapshot(nint controller, out bool attached)
    {
        attached = false;
        if (controller == 0)
        {
            return GamepadButton.None;
        }

        if (getAttached is not null)
        {
            try
            {
                if (getAttached(controller) == 0)
                {
                    return GamepadButton.None;
                }
            }
            catch
            {
                return GamepadButton.None;
            }
        }

        attached = true;
        inSnapshot = true;
        try
        {
            var buttons = GamepadButton.None;
            foreach (var (sdlButton, button) in ButtonMap)
            {
                if (CallOriginalGetButton(controller, sdlButton) != 0)
                {
                    buttons |= button;
                }
            }

            return buttons;
        }
        finally
        {
            inSnapshot = false;
        }
    }

    private static GamepadButton MapButton(int sdlButton)
    {
        foreach (var (candidate, button) in ButtonMap)
        {
            if (candidate == sdlButton)
            {
                return button;
            }
        }

        return GamepadButton.None;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        // Detours out first, then the policy, so a call already in flight cannot find
        // the menu being torn down half way through deciding.
        DisableHooks();
        try
        {
            capture.Close();
        }
        catch (Exception ex)
        {
            log?.Invoke($"Controller navigation capture could not be closed cleanly: {ex.Message}");
        }
    }

    private void DisableHooks()
    {
        Disable(closeHook);
        Disable(getButtonHook);

        void Disable<T>(IHook<T>? hook) where T : Delegate
        {
            try
            {
                if (hook is { IsHookActivated: true })
                {
                    hook.Disable();
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Controller navigation detour could not be removed: {ex.Message}");
            }
        }
    }

    internal readonly record struct SdlExports(nint GetButton, nint Update, nint Close, nint GetAttached);

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern nint GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern nint GetProcAddress(nint module, string procedureName);
    }
}
