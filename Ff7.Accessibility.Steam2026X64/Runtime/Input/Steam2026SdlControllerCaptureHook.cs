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
///
/// <para>The latch moves. It used to be permanent, and that was the reported fault: a
/// host with two pads open reads both every frame, so whichever one SDL enumerated
/// first took the menu during startup - before the player had touched anything - and
/// the pad actually in their hands could never produce an edge. Its ordinary controls
/// kept working, because a device we do not own is handed straight back to the game,
/// so the failure was silent in the log and in speech alike. A device that is not the
/// owner is now asked one question per read, the stick click, and a <em>fresh</em>
/// click hands the menu over - but only while the menu is holding nothing, so an open
/// menu cannot be taken from the player using it and the tail that keeps a closing
/// press away from the game always finishes on the pad that made it.</para>
/// </summary>
internal sealed class Steam2026SdlControllerCaptureHook : IDisposable
{
    private const string SdlModule = "SDL2.dll";

    /// <summary>
    /// SDL's own number for the right stick click. It is the only button a device that
    /// is not the owner is ever asked about, because it is the only one that can make
    /// it the owner.
    /// </summary>
    private const int SdlRightStick = 8;

    /// <summary>
    /// Caps remembered devices so a host handing out fresh pointers cannot grow the
    /// table without bound.
    /// </summary>
    private const int MaximumTrackedDevices = 8;

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

    // Diagnostics only, and all optional: a build of SDL that does not export one of
    // these simply leaves that field unknown in the census line. None of them is on
    // the path that decides anything.
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint GetNameDelegate(nint controller);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int GetControllerTypeDelegate(nint controller);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int HasButtonDelegate(nint controller, int button);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate ushort GetIdentifierDelegate(nint controller);

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

    private readonly GetNameDelegate? getName;
    private readonly GetControllerTypeDelegate? getControllerType;
    private readonly HasButtonDelegate? hasButton;
    private readonly GetIdentifierDelegate? getVendor;
    private readonly GetIdentifierDelegate? getProduct;

    /// <summary>
    /// Every device the game has read through us, capped at
    /// <see cref="MaximumTrackedDevices"/>. Guarded by <see cref="deviceSync"/>.
    /// </summary>
    private readonly List<TrackedDevice> devices = new(MaximumTrackedDevices);

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

        // Diagnostics. Bound after the detours are live because none of them can fail
        // the install: an SDL without one of these just leaves that field unknown.
        getName = Bind<GetNameDelegate>(exports.GetName);
        getControllerType = Bind<GetControllerTypeDelegate>(exports.GetControllerType);
        hasButton = Bind<HasButtonDelegate>(exports.HasButton);
        getVendor = Bind<GetIdentifierDelegate>(exports.GetVendor);
        getProduct = Bind<GetIdentifierDelegate>(exports.GetProduct);

        static T? Bind<T>(nint address) where T : Delegate =>
            address == 0 ? null : Marshal.GetDelegateForFunctionPointer<T>(address);
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
            diagnostic = "Controller navigation capture installed over SDL2!SDL_GameControllerGetButton (multi-controller R3 ownership).";
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
            NativeMethods.GetProcAddress(module, "SDL_GameControllerGetAttached"),
            NativeMethods.GetProcAddress(module, "SDL_GameControllerName"),
            NativeMethods.GetProcAddress(module, "SDL_GameControllerGetType"),
            NativeMethods.GetProcAddress(module, "SDL_GameControllerHasButton"),
            NativeMethods.GetProcAddress(module, "SDL_GameControllerGetVendor"),
            NativeMethods.GetProcAddress(module, "SDL_GameControllerGetProduct"));
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
                    ReleaseOwnershipLocked();
                }

                // Everything we knew about it goes with it, owner or not. SDL is free
                // to hand this pointer back out for a different device, and the click
                // this one was holding when it left must not read as a fresh press on
                // whatever arrives next.
                RemoveDeviceLocked(controller);
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
    /// Whether this device is the one the menu is listening to. A device we do not own
    /// is read and filtered by nobody - its buttons go back to the game untouched.
    ///
    /// <para>The first device the game reads is latched exactly as it always was. On a
    /// single pad that happens during startup, long before the player reaches for
    /// anything, and nothing about that case changes. What is new is that another pad
    /// can take the menu over by asking for it.</para>
    /// </summary>
    private bool TryOwn(nint controller)
    {
        if (controller == 0)
        {
            return false;
        }

        nint owner;
        lock (deviceSync)
        {
            owner = latchedController;
        }

        if (owner == controller)
        {
            return true;
        }

        return owner == 0 ? TryLatchUnowned(controller) : TryTakeOver(controller);
    }

    /// <summary>Nobody owns the menu, so the device reading it now does.</summary>
    private bool TryLatchUnowned(nint controller)
    {
        _ = ObserveStickClickEdge(controller);

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

            ReleaseOwnershipLocked();
            latchedController = controller;
        }

        // A newly latched device starts neutral: whatever is already held on it has to
        // be released before it can mean anything.
        _ = capture.ObserveRawPoll(GamepadSnapshot.Disconnected, now());
        return true;
    }

    /// <summary>
    /// Somebody else owns the menu. This device may take it, but only by asking for it
    /// with a stick click we have watched go down, and only while the menu is holding
    /// nothing at all.
    /// </summary>
    private bool TryTakeOver(nint controller)
    {
        if (!ObserveStickClickEdge(controller))
        {
            return false;
        }

        // An open menu is not somebody else's to take: the player reading a list must
        // not have it pulled out from under them. Nor is a menu that has closed but is
        // still keeping the press that closed it away from the game - handing over
        // mid-tail would release that press into the game, which is the whole thing
        // the tail exists to prevent.
        if (capture.IsSuppressing)
        {
            return false;
        }

        var buttons = ReadSnapshot(controller, out var attached);
        if (!attached)
        {
            ForgetDevice(controller);
            return false;
        }

        nint previous;
        lock (deviceSync)
        {
            previous = latchedController;
            if (previous == controller)
            {
                return true;
            }

            ReleaseOwnershipLocked();
            latchedController = controller;
        }

        // Handed over neutral, and then given back everything this device is already
        // holding apart from the click that asked for it. Nothing of the old owner's
        // held state survives that, and the click reads as the press it is rather than
        // being blocked along with whatever else the player has a thumb resting on.
        _ = capture.ObserveRawPoll(GamepadSnapshot.Disconnected, now());
        _ = capture.ObserveRawPoll(
            new GamepadSnapshot(true, 0, 0, buttons & ~GamepadButton.RightThumb), now());

        log?.Invoke(
            $"Controller navigation moved to controller 0x{controller:X} " +
            $"from 0x{previous:X}: the right stick was clicked on it.");
        return true;
    }

    /// <summary>
    /// Records what this device's stick click is doing and says whether it has just
    /// gone down.
    ///
    /// <para>The very first thing we learn about a device is never a press. A click
    /// that was already held the first time we saw the device is a resting state, a
    /// stuck stick, or a thumb that happened to be down - not a request - and it has
    /// to be released before it can mean anything.</para>
    /// </summary>
    private bool ObserveStickClickEdge(nint controller)
    {
        var down = ReadOneButton(controller, SdlRightStick) != 0;

        bool firstSight;
        bool rising;
        lock (deviceSync)
        {
            var device = FindDeviceLocked(controller);
            firstSight = device is null;
            if (device is null)
            {
                rising = false;
                AddDeviceLocked(controller, down);
            }
            else
            {
                rising = down && !device.RightThumbWasDown;
                device.RightThumbWasDown = down;
            }
        }

        if (firstSight)
        {
            LogDeviceSeen(controller);
        }

        return rising;
    }

    private TrackedDevice? FindDeviceLocked(nint controller)
    {
        foreach (var device in devices)
        {
            if (device.Controller == controller)
            {
                return device;
            }
        }

        return null;
    }

    private void AddDeviceLocked(nint controller, bool rightThumbDown)
    {
        if (devices.Count >= MaximumTrackedDevices)
        {
            // Never silently: a host that reaches this is doing something we have not
            // seen, and the line is the only way anyone would find out.
            var evicted = devices[0];
            devices.RemoveAt(0);
            log?.Invoke(
                $"Controller navigation is tracking more than {MaximumTrackedDevices} " +
                $"devices and has forgotten the oldest, 0x{evicted.Controller:X}.");
        }

        devices.Add(new TrackedDevice(controller, rightThumbDown));
    }

    private void ForgetDevice(nint controller)
    {
        lock (deviceSync)
        {
            RemoveDeviceLocked(controller);
        }
    }

    private void RemoveDeviceLocked(nint controller)
    {
        for (var index = 0; index < devices.Count; index++)
        {
            if (devices[index].Controller == controller)
            {
                devices.RemoveAt(index);
                return;
            }
        }
    }

    /// <summary>
    /// One button, read through the original getter, with the policy told not to treat
    /// the read as the game's. This is the single question a device that is not the
    /// owner is ever asked.
    /// </summary>
    private byte ReadOneButton(nint controller, int sdlButton)
    {
        inSnapshot = true;
        try
        {
            return CallOriginalGetButton(controller, sdlButton);
        }
        finally
        {
            inSnapshot = false;
        }
    }

    /// <summary>
    /// One line per device, the first time the game reads it. This is the only record
    /// of what is actually plugged in, which is what a report of "the menu does nothing
    /// on my pad" needs to be answerable at all. Built once per device and never on a
    /// poll that decides anything.
    /// </summary>
    private void LogDeviceSeen(nint controller)
    {
        if (log is null)
        {
            return;
        }

        try
        {
            var name = "unknown";
            if (getName is not null)
            {
                var text = getName(controller);
                name = text == 0 ? "unnamed" : Marshal.PtrToStringUTF8(text) ?? "unnamed";
            }

            var type = getControllerType is null ? "unknown" : getControllerType(controller).ToString();
            var stickClick = hasButton is null
                ? "unknown"
                : hasButton(controller, SdlRightStick) != 0 ? "yes" : "no";
            var leftShoulder = hasButton is null ? "unknown" : hasButton(controller, 9) != 0 ? "yes" : "no";
            var rightShoulder = hasButton is null ? "unknown" : hasButton(controller, 10) != 0 ? "yes" : "no";
            var vendor = getVendor is null ? "unknown" : $"0x{getVendor(controller):X4}";
            var product = getProduct is null ? "unknown" : $"0x{getProduct(controller):X4}";

            log.Invoke(
                $"Controller navigation saw a device: 0x{controller:X} name=\"{name}\" " +
                $"type={type} vendor={vendor} product={product} rightStickClick={stickClick} " +
                $"leftShoulder={leftShoulder} rightShoulder={rightShoulder}.");
        }
        catch (Exception ex)
        {
            log.Invoke($"Controller navigation could not describe a device: {ex.Message}");
        }
    }

    /// <summary>
    /// What we remember about one device between reads: enough to tell a stick click
    /// that has just gone down from one that was already held, and no more.
    /// </summary>
    private sealed class TrackedDevice(nint controller, bool rightThumbWasDown)
    {
        internal nint Controller { get; } = controller;

        internal bool RightThumbWasDown { get; set; } = rightThumbWasDown;
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
                    ReleaseOwnershipLocked();
                }

                // As in OnClose: a device that has stopped answering is gone, and its
                // held state goes with it rather than waiting to be mistaken for a
                // press when it comes back.
                RemoveDeviceLocked(controller);
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

            // The owner's stick click is recorded from the sweep it is already doing,
            // so that if the menu later changes hands, this device's own click has to
            // be released and pressed again to take it back.
            var device = FindDeviceLocked(controller);
            if (device is not null)
            {
                device.RightThumbWasDown =
                    (buttons & GamepadButton.RightThumb) == GamepadButton.RightThumb;
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

    /// <summary>
    /// Stops listening to whatever we were listening to. This is about ownership only -
    /// the device stays in the table with its stick-click state intact, because a pad
    /// that has merely lost the menu is still plugged in and must not have its next
    /// click read against nothing.
    /// </summary>
    private void ReleaseOwnershipLocked()
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

    /// <param name="GetName">
    /// From here down the entries are diagnostics only, and every one of them may be
    /// zero. They are read once per device, for the census line, and never on the path
    /// that decides anything.
    /// </param>
    internal readonly record struct SdlExports(
        nint GetButton,
        nint Update,
        nint Close,
        nint GetAttached,
        nint GetName,
        nint GetControllerType,
        nint HasButton,
        nint GetVendor,
        nint GetProduct);

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
