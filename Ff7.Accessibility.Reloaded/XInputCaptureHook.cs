using System.Runtime.InteropServices;
using Ff7.Accessibility.Core;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Reloaded;

[global::Reloaded.Hooks.Definitions.X86.Function(
    global::Reloaded.Hooks.Definitions.X86.CallingConventions.Stdcall)]
[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate int XInputGetStateDelegate(int userIndex, out XInputCaptureHook.XInputState state);

/// <summary>
/// Takes the navigation menu's buttons out of the controller state the legacy game is
/// given, in the same read that produced them.
///
/// <para>The seam is <c>XInputGetState</c>. The installed build runs FFNx 1.24.3,
/// whose <c>Gamepad::Refresh</c> is <c>XInputGetState(cId, ...)</c> and which replaces
/// the game's own <c>0x0041F7D8</c> status builder with it; the shortcut mode in
/// <c>gamehacks.cpp</c> polls the same API separately, so a hook at the game's builder
/// would have missed it and this does not. Nothing here rests on a struct offset or an
/// address: the ABI, the <c>ERROR_DEVICE_NOT_CONNECTED</c> return and the
/// <c>XINPUT_GAMEPAD_*</c> bits are published contract.</para>
///
/// <para>Every XInput module already loaded is hooked, deduplicated by the address the
/// export resolves to. Picking one preferred DLL and loading it if absent proves
/// nothing about which one the game is polling, and would have left R3 silent while
/// reporting success.</para>
/// </summary>
public sealed class XInputCaptureHook : IDisposable
{
    private const int ErrorSuccess = 0;

    private readonly List<HookEntry> hooks = [];
    private readonly ControllerNavigationCapture capture;
    private readonly Func<DateTime> now;
    private readonly Action<string>? log;

    [ThreadStatic]
    private static bool inDetour;

    private int latchedUserIndex = -1;
    private int disposed;

    private XInputCaptureHook(
        IReloadedHooks reloadedHooks,
        IReadOnlyList<(string Module, nint Procedure)> procedures,
        Func<XInputCaptureHook, ControllerNavigationCapture> createCapture,
        Func<DateTime>? now,
        Action<string>? log)
    {
        this.now = now ?? (static () => DateTime.UtcNow);
        this.log = log;
        capture = createCapture(this);

        // Created first, activated afterwards. A cohort that throws half way through
        // leaving live detours behind is the one failure here that cannot be
        // recovered from at runtime.
        try
        {
            foreach (var (_, procedure) in procedures)
            {
                // One detour per export, each closing over its own entry so it calls
                // its own trampoline. Sharing one delegate would make every module's
                // detour call the first module's original, which is a different code
                // path to the one the caller came through.
                var entry = new HookEntry();
                entry.Detour = (int userIndex, out XInputState state) =>
                    OnGetState(entry, userIndex, out state);
                entry.Hook = reloadedHooks.CreateHook(entry.Detour, (long)procedure);
                entry.Original = entry.Hook.OriginalFunction;
                hooks.Add(entry);
            }

            foreach (var entry in hooks)
            {
                entry.Hook!.Activate();
            }
        }
        catch
        {
            DisableHooks();
            throw;
        }
    }

    public ControllerNavigationCapture Capture => capture;

    /// <summary>
    /// Builds the detour over injected originals, with no hooking backend. Used to
    /// drive the nested-export case - xinput9_1_0 forwarding into xinput1_4 - which is
    /// what produced the runaway category cycling and cannot be reproduced any other
    /// way without a second XInput runtime.
    /// </summary>
    internal static XInputCaptureHook CreateForDetourTest(
        Func<Func<bool>, ControllerNavigationCapture> createCapture,
        IReadOnlyList<string> exportNames,
        Func<DateTime>? now = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(createCapture);
        return new XInputCaptureHook(createCapture, exportNames, now, log);
    }

    private XInputCaptureHook(
        Func<Func<bool>, ControllerNavigationCapture> createCapture,
        IReadOnlyList<string> exportNames,
        Func<DateTime>? now,
        Action<string>? log)
    {
        this.now = now ?? (static () => DateTime.UtcNow);
        this.log = log;
        XInputCaptureHook? self = null;
        capture = createCapture(() => self?.IsInstalled == true);
        self = this;
        foreach (var _ in exportNames)
        {
            hooks.Add(new HookEntry());
        }

        HookedModules = exportNames.ToArray();
    }

    /// <summary>Calls the detour for one export, as the game would. Test seam.</summary>
    internal int InvokeDetourForTest(int exportIndex, int userIndex, out XInputState state) =>
        OnGetState(hooks[exportIndex], userIndex, out state);

    /// <summary>Supplies what one export's original does. Test seam.</summary>
    internal void SetOriginalForTest(int exportIndex, XInputGetStateDelegate original) =>
        hooks[exportIndex].Original = original;

    public IReadOnlyList<string> HookedModules { get; private set; } = [];

    public bool IsInstalled
    {
        get
        {
            if (Volatile.Read(ref disposed) != 0)
            {
                return false;
            }

            foreach (var entry in hooks)
            {
                // A test-constructed entry has no backend hook but a live original,
                // and the policy must believe suppression is possible for it.
                if (entry.Hook is null
                    ? entry.Original is not null
                    : entry.Hook is { IsHookActivated: true, IsHookEnabled: true })
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Installs over every XInput runtime this process already has open.
    ///
    /// <para>Failure costs the controller menu and nothing else - every keyboard
    /// binding still works and the game's input is untouched, because no detour went
    /// in. So it reports and carries on.</para>
    /// </summary>
    /// <param name="loadIfAbsent">
    /// Whether to open an XInput module when the process has none. Off for the smoke
    /// test, which pre-loads exactly the one it means to exercise.
    /// </param>
    public static bool TryInstall(
        IReloadedHooks reloadedHooks,
        Func<Func<bool>, ControllerNavigationCapture> createCapture,
        out XInputCaptureHook installed,
        out string diagnostic,
        Func<DateTime>? now = null,
        Action<string>? log = null,
        bool loadIfAbsent = true)
    {
        ArgumentNullException.ThrowIfNull(reloadedHooks);
        ArgumentNullException.ThrowIfNull(createCapture);
        installed = null!;

        var procedures = ResolveLoadedProcedures();
        if (procedures.Count == 0 && loadIfAbsent)
        {
            // Nothing open yet. The game loads XInput when it first looks for a pad,
            // so this is normal early on and the caller retries; opening one ourselves
            // is only a last resort so the menu can work before then.
            foreach (var moduleName in NativeMethods.XInputModules)
            {
                if (NativeMethods.LoadLibrary(moduleName) != 0)
                {
                    break;
                }
            }

            procedures = ResolveLoadedProcedures();
        }

        if (procedures.Count == 0)
        {
            diagnostic = "No XInput runtime is loaded; controller navigation will retry.";
            return false;
        }

        try
        {
            XInputCaptureHook? candidate = null;
            candidate = new XInputCaptureHook(
                reloadedHooks,
                procedures,
                _ => createCapture(() => candidate?.IsInstalled == true),
                now,
                log)
            {
                HookedModules = procedures.Select(entry => entry.Module).ToArray(),
            };
            installed = candidate;
            diagnostic =
                "Controller navigation capture installed over " +
                $"{string.Join(", ", candidate.HookedModules)}!XInputGetState.";
            return true;
        }
        catch (Exception ex)
        {
            // The message alone is often "a type initializer threw", which says
            // nothing about what is actually missing. The chain is what identifies it.
            diagnostic = $"XInputGetState could not be hooked: {Describe(ex)}";
            return false;
        }
    }

    private static string Describe(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join(" -> ", parts);
    }

    /// <summary>
    /// Every distinct <c>XInputGetState</c> in the process. Two module names can
    /// forward to one implementation, so the addresses are deduplicated: hooking the
    /// same code twice would run the policy twice for one poll.
    /// </summary>
    private static List<(string Module, nint Procedure)> ResolveLoadedProcedures()
    {
        var found = new List<(string Module, nint Procedure)>();
        var seen = new HashSet<nint>();
        foreach (var moduleName in NativeMethods.XInputModules)
        {
            nint module;
            try
            {
                module = NativeMethods.GetModuleHandle(moduleName);
            }
            catch
            {
                continue;
            }

            if (module == 0)
            {
                continue;
            }

            var procedure = NativeMethods.GetProcAddress(module, "XInputGetState");
            if (procedure != 0 && seen.Add(procedure))
            {
                found.Add((moduleName, procedure));
            }
        }

        return found;
    }

    private int OnGetState(HookEntry entry, int userIndex, out XInputState state)
    {
        // The guard has to cover the call to the original, not just what follows it.
        //
        // xinput9_1_0 forwards into xinput1_4, and both are hooked, so one game poll
        // enters this twice: outer 9_1_0, then inner 1_4. With the guard set after the
        // call, the inner run saw the raw state, decided, and stripped the buttons out
        // of the very buffer the outer run then observed - so the outer told the edge
        // tracker the button had been released, and the next frame looked like a fresh
        // press. Held bumpers cycled categories about twenty times a second.
        if (inDetour || Volatile.Read(ref disposed) != 0)
        {
            return entry.Original(userIndex, out state);
        }

        inDetour = true;
        try
        {
            var result = entry.Original(userIndex, out state);
            try
            {
                Filter(userIndex, result, ref state);
            }
            catch (Exception ex)
            {
                // The game is waiting on this call. It gets its own state.
                log?.Invoke($"Controller navigation capture failed and was bypassed: {ex.Message}");
            }

            return result;
        }
        finally
        {
            inDetour = false;
        }
    }

    /// <summary>Takes the menu's buttons out of one poll of the latched slot.</summary>
    private void Filter(int userIndex, int result, ref XInputState state)
    {
        var connected = result == ErrorSuccess;
        var latched = Volatile.Read(ref latchedUserIndex);
        if (connected && latched < 0)
        {
            Interlocked.CompareExchange(ref latchedUserIndex, userIndex, -1);
            latched = Volatile.Read(ref latchedUserIndex);
        }
        else if (!connected && latched == userIndex)
        {
            // The pad we were listening to has gone. The policy is told, so the menu
            // closes and nothing held survives the reconnect, and the next connected
            // slot may be latched.
            Interlocked.Exchange(ref latchedUserIndex, -1);
            _ = capture.ObserveRawPoll(GamepadSnapshot.Disconnected, now());
            return;
        }

        // Every other slot is somebody else's device. Reading it would make the
        // player's buttons do nothing; filtering it would break a second player.
        if (!connected || latched != userIndex)
        {
            return;
        }

        var raw = new GamepadSnapshot(
            true, userIndex, state.PacketNumber, (GamepadButton)state.Gamepad.Buttons);
        var strip = capture.ObserveRawPoll(raw, now());
        if (strip != GamepadButton.None)
        {
            state.Gamepad.Buttons = (ushort)(state.Gamepad.Buttons & ~(ushort)strip);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        // Detours out first, then the policy. The other order lets a call already in
        // flight find a closed menu half way through deciding.
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
        foreach (var entry in hooks)
        {
            try
            {
                if (entry.Hook is { IsHookActivated: true })
                {
                    entry.Hook.Disable();
                }
            }
            catch (Exception ex)
            {
                // The delegates stay rooted in this object either way, so a detour the
                // backend could not remove still lands on managed code rather than on
                // a collected stub.
                log?.Invoke($"Controller navigation detour could not be removed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// One hooked export. The detour is held here for the life of the object - a
    /// collected delegate is a jump into freed memory on the game's input path, and
    /// that stays true even for a detour the backend failed to remove.
    /// </summary>
    private sealed class HookEntry
    {
        public XInputGetStateDelegate? Detour { get; set; }

        public IHook<XInputGetStateDelegate>? Hook { get; set; }

        /// <summary>This export's own trampoline, or an injected one under test.</summary>
        public XInputGetStateDelegate Original { get; set; } = null!;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    private static class NativeMethods
    {
        internal static readonly string[] XInputModules =
        [
            "xinput1_4.dll",
            "xinput1_3.dll",
            "xinput9_1_0.dll",
        ];

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern nint GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, EntryPoint = "LoadLibraryA")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern nint LoadLibrary(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern nint GetProcAddress(nint module, string procedureName);
    }
}
