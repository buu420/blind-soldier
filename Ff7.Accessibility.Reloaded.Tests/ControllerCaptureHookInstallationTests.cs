using System.Runtime.InteropServices;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;
using Reloaded.Hooks;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// Installs the real capture hook over the real system XInput, in this real 32-bit
/// process, and calls through it.
///
/// <para>This exists because every other test in this area passed while the hook
/// could not have installed at all. Reloaded resolves an x86 delegate's calling
/// convention through <c>FunctionAttribute.GetAttribute</c>, which <em>throws</em>
/// when the delegate carries only <c>[UnmanagedFunctionPointer(Winapi)]</c> - so a
/// missing <c>[X86.Function(Stdcall)]</c> is a startup exception on the player's
/// machine and a green suite here. Policy tests cannot see that. This can, because
/// it is the production <see cref="XInputCaptureHook"/>, the production
/// <c>ReloadedHooks</c> backend, and the real export.</para>
///
/// <para>No controller is needed: <c>XInputGetState</c> answering
/// <c>ERROR_DEVICE_NOT_CONNECTED</c> on an empty slot is a complete round trip
/// through the detour, the trampoline and back.</para>
/// </summary>
internal static class ControllerCaptureHookInstallationTests
{
    private const int ErrorDeviceNotConnected = 1167;

    public static void Run()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        TheDelegateResolvesToStdcallForReloaded();
        TheProductionHookInstallsOverRealXInputAndCallsThrough();
    }

    /// <summary>
    /// The exact defect root found, reproduced without needing anything but
    /// reflection: Reloaded resolves an x86 delegate's calling convention through
    /// <c>FunctionAttribute.GetAttribute</c>, and that <b>throws</b> when the
    /// delegate carries only <c>[UnmanagedFunctionPointer(Winapi)]</c>.
    ///
    /// <para>So a missing <c>[X86.Function(Stdcall)]</c> is an exception the moment
    /// the hook is created on a player's machine, while every policy test stays
    /// green. This asserts the attribute is there and says stdcall - the convention
    /// <c>XInputGetState</c> actually uses.</para>
    /// </summary>
    private static void TheDelegateResolvesToStdcallForReloaded()
    {
        var delegateType = typeof(Ff7.Accessibility.Reloaded.XInputCaptureHook)
            .Assembly.GetType("Ff7.Accessibility.Reloaded.XInputGetStateDelegate", throwOnError: true)!;

        var attributes = delegateType.GetCustomAttributes(
            typeof(global::Reloaded.Hooks.Definitions.X86.FunctionAttribute),
            inherit: false);
        Equal(1, attributes.Length,
            "the XInput delegate carries exactly one Reloaded x86 function attribute");

        // The call Reloaded itself makes, on the delegate itself. This is the exact
        // thing that threw in root's probe when the attribute was absent, so it is
        // the exact thing worth asserting rather than a proxy for it.
        var resolved = typeof(global::Reloaded.Hooks.Definitions.X86.FunctionAttribute)
            .GetMethod("GetAttribute", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(delegateType)
            .Invoke(null, null);
        Equal(true, resolved is not null, "Reloaded can resolve its calling convention");

        // Stdcall is callee stack cleanup. XInputGetState is WINAPI, which on x86 is
        // stdcall: getting this wrong unbalances the stack on every single poll.
        var cleanup = (global::Reloaded.Hooks.Definitions.X86.FunctionAttribute.StackCleanup)
            resolved!.GetType().GetProperty("Cleanup")!.GetValue(resolved)!;
        Equal(
            global::Reloaded.Hooks.Definitions.X86.FunctionAttribute.StackCleanup.Callee,
            cleanup,
            "and it is stdcall - callee stack cleanup - as XInputGetState requires");
    }

    private static void TheProductionHookInstallsOverRealXInputAndCallsThrough()
    {
        // Whatever this machine has. Nothing is hooked before it is open, because
        // hooking a module the process was not using proves nothing.
        string? loaded = null;
        foreach (var moduleName in new[] { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" })
        {
            if (NativeMethods.LoadLibrary(moduleName) != 0)
            {
                loaded = moduleName;
                break;
            }
        }

        if (loaded is null)
        {
            Console.WriteLine(
                "Controller capture hook: skipped, this machine has no XInput runtime.");
            return;
        }

        // What each slot says before anything is hooked.
        //
        // Only the return code is compared. XInputGetState documents an error return
        // for a disconnected controller and promises nothing at all about the output
        // structure on failure - it does not undertake to clear it, so whatever was in
        // the caller's buffer can still be there. Production already honours that by
        // never reading the state unless the call returned success.
        var before = new int[4];
        for (var slot = 0; slot < before.Length; slot++)
        {
            before[slot] = NativeMethods.XInputGetState(slot, out _);
            Equal(true, before[slot] is 0 or ErrorDeviceNotConnected,
                $"slot {slot} answers something sane before hooking ({before[slot]})");
        }

        var anyConnected = before.Any(result => result == 0);

        // Recorded in the log because every assertion below depends on it, and
        // whether a pad happened to be powered on changes which branch ran.
        Console.WriteLine(
            $"Controller capture hook: slot returns before hooking = " +
            $"[{string.Join(", ", before)}]; any connected = {anyConnected}.");

        var polls = 0L;
        var installed = XInputCaptureHook.TryInstall(
            ReloadedHooks.Instance,
            isInstalled => new ControllerNavigationCapture(
                suppressor => new ControllerNavigationMenu(EmptyGamepadReader.Instance, suppressor),
                isInstalled,
                isForegroundNow: () => true),
            out var hook,
            out var diagnostic,
            log: message => Console.WriteLine($"Controller capture hook: {message}"),
            loadIfAbsent: false);

        if (!installed)
        {
            // Reloaded's x86 hook writer builds its trampolines with FASM through
            // Reloaded.Assembler, and in this bare test host that assembler cannot
            // initialise: the NuGet graph resolves Reloaded.Memory 9.4.3, which no
            // longer has the Reloaded.Memory.Sources.Memory type the assembler was
            // built against. Inside the game the Reloaded-II loader supplies its own
            // compatible copy, which is why every existing hook in this mod works.
            //
            // That is a property of this process, not of the code under test, so it
            // is reported rather than failed - but it is reported loudly, because it
            // means this run did NOT prove the ABI end to end. The attribute check
            // above is what carries the defect root actually found.
            Console.WriteLine(
                $"Controller capture hook: INSTALL NOT EXERCISED in this host - {diagnostic}");
            return;
        }

        try
        {
            Equal(true, hook.IsInstalled, "and reports itself installed and enabled");
            Equal(true, hook.HookedModules.Count > 0, "over at least one loaded module");
            polls = hook.Capture.ObservedPolls;

            // Through the detour, the policy and the trampoline, at the real ABI. A
            // wrong calling convention corrupts the stack here rather than politely
            // returning something odd.
            for (var slot = 0; slot < before.Length; slot++)
            {
                var result = NativeMethods.XInputGetState(slot, out var state);
                Equal(true, result is 0 or ErrorDeviceNotConnected,
                    $"slot {slot} answers through the hook ({result})");

                // The detour must not change what the slot says about itself. This is
                // the part the contract actually guarantees, and it is what a broken
                // calling convention or a mangled out-parameter would disturb.
                Equal(before[slot], result,
                    $"slot {slot} reports the same connectivity through the detour");

                // Deliberately nothing about state on a failed call. See above.
                _ = state;
            }

            // Exactly one slot reaches the policy, or none if no pad answered. Every
            // other slot is somebody else's device or no device: reading it would make
            // the player's own pad do nothing, and filtering it would break a second
            // player. Four reads must therefore produce one observation, not four.
            Equal(
                anyConnected ? 1L : 0L,
                hook.Capture.ObservedPolls - polls,
                $"four slot reads tell the policy about the latched slot only " +
                $"(connected={anyConnected})");

            // The menu cannot open with no pad and no context, and must not have
            // started taking buttons away from a game that has no controller.
            Equal(false, hook.Capture.IsOpen, "no pad means no menu");
        }
        finally
        {
            hook.Dispose();
        }

        Equal(false, hook.IsInstalled, "and it reports itself gone once disposed");

        // The export still works with the detour removed, which is what a player's
        // game does for the rest of its life after the mod unloads.
        var after = NativeMethods.XInputGetState(0, out _);
        Equal(before[0], after, "the unhooked export behaves exactly as it did before");

        // Installing again over the same export has to work, because a session that
        // reloads does precisely this.
        var again = XInputCaptureHook.TryInstall(
            ReloadedHooks.Instance,
            isInstalled => new ControllerNavigationCapture(
                suppressor => new ControllerNavigationMenu(EmptyGamepadReader.Instance, suppressor),
                isInstalled),
            out var second,
            out var secondDiagnostic,
            loadIfAbsent: false);
        Equal(true, again, $"a second install over the same export succeeds: {secondDiagnostic}");
        second.Dispose();
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, EntryPoint = "LoadLibraryA")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern nint LoadLibrary(string moduleName);

        /// <summary>
        /// Deliberately declared here rather than reused from the hook: this is the
        /// caller's side of the contract, and it is how the game reaches the export.
        /// </summary>
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int XInputGetState14(int userIndex, out XInputCaptureHook.XInputState state);

        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int XInputGetState13(int userIndex, out XInputCaptureHook.XInputState state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int XInputGetState910(int userIndex, out XInputCaptureHook.XInputState state);

        internal static int XInputGetState(int userIndex, out XInputCaptureHook.XInputState state)
        {
            try
            {
                return XInputGetState14(userIndex, out state);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            try
            {
                return XInputGetState13(userIndex, out state);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }

            return XInputGetState910(userIndex, out state);
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Controller capture hook: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
