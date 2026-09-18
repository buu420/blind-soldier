using System.Runtime.InteropServices;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Steam2026X64.Runtime.Input;
using Reloaded.Hooks;

/// <summary>
/// Installs the production SDL capture over the installed <c>SDL2.dll</c>, in this
/// real 64-bit process, and drives it with a virtual controller.
///
/// <para>SDL's own <c>SDL_JoystickAttachVirtual</c> gives a real
/// <c>SDL_GameController*</c> with real getter calls and no driver and no hardware,
/// so the filtering can be exercised exactly as the game would: the buttons the game
/// asks about, in the order it asks, with no assumption that it ever calls
/// <c>SDL_GameControllerUpdate</c>.</para>
/// </summary>
internal static class Steam2026SdlControllerCaptureHookTests
{
    private const string InstalledSdl =
        @"C:\Program Files (x86)\Steam\steamapps\common\FINAL FANTASY VII Steam Edition\SDL2.dll";

    private const uint InitGameController = 0x00002000;
    private const int TypeGameController = 1;

    // SDL_GameControllerButton, from the published enum.
    private const int ButtonA = 0;
    private const int ButtonRightStick = 8;
    private const int ButtonDPadDown = 12;

    public static void Run()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(InstalledSdl))
        {
            Console.WriteLine($"SDL controller capture: skipped, {InstalledSdl} is not present.");
            return;
        }

        if (Sdl.LoadLibrary(InstalledSdl) == 0)
        {
            Console.WriteLine("SDL controller capture: skipped, the installed SDL2.dll would not load.");
            return;
        }

        if (Sdl.InitSubSystem(InitGameController) != 0)
        {
            Console.WriteLine(
                $"SDL controller capture: skipped, SDL would not start: {Sdl.GetErrorText()}");
            return;
        }

        try
        {
            TheProductionHookInstallsOverTheInstalledSdlAndFilters();
        }
        finally
        {
            Sdl.QuitSubSystem(InitGameController);
        }
    }

    private static void TheProductionHookInstallsOverTheInstalledSdlAndFilters()
    {
        var installed = Steam2026SdlControllerCaptureHook.TryInstall(
            ReloadedHooks.Instance,
            isInstalled => new ControllerNavigationCapture(
                suppressor => new ControllerNavigationMenu(EmptyGamepadReader.Instance, suppressor),
                isInstalled,
                isForegroundNow: () => true),
            out var hook,
            out var diagnostic,
            log: message => Console.WriteLine($"SDL controller capture: {message}"));
        if (!installed)
        {
            // The bare NuGet test host cannot initialise Reloaded's hook backend:
            // the graph resolves Reloaded.Memory 9.4.3, which dropped the
            // Sources.Memory type Reloaded.Assembler was built against. The isolated
            // host runner substitutes the loader's own copy and does exercise this.
            // Reported loudly rather than passed quietly, so a run that proves
            // nothing cannot be mistaken for one that does.
            Console.WriteLine(
                $"SDL controller capture: INSTALL NOT EXERCISED in this host - {diagnostic}");
            return;
        }

        var device = -1;
        var controller = (nint)0;
        try
        {
            Equal(true, hook.IsInstalled, "and reports itself installed and enabled");

            // A null controller must be handled without touching anything: SDL
            // answers, we own nothing, the game gets SDL's answer.
            Equal((byte)0, Sdl.GetButton(0, ButtonA), "a null controller answers zero through the hook");
            Equal((nint)0, hook.LatchedController, "and nothing is latched to it");

            device = Sdl.AttachVirtual(TypeGameController, axes: 6, buttons: 15, hats: 1);
            if (device < 0)
            {
                Console.WriteLine(
                    "SDL controller capture: virtual controller unavailable " +
                    $"({Sdl.GetErrorText()}); install and lifetime were still exercised.");
                return;
            }

            controller = Sdl.GameControllerOpen(device);
            Equal(true, controller != 0, $"the virtual controller opens: {Sdl.GetErrorText()}");

            var joystick = Sdl.GameControllerGetJoystick(controller);
            Equal(true, joystick != 0, "and exposes its joystick");

            // The context the worker would publish, and a neutral first read so the
            // newly latched device starts with nothing held.
            hook.Capture.PublishContext(
                ControllerNavigationDomain.Field, true, true, false, DateTime.UtcNow, identity: 500);
            _ = Sdl.GetButton(controller, ButtonA);
            Equal(controller, hook.LatchedController, "the device the game reads is the one we latch");

            // The case root named. R3 goes down, and the game asks only about A and
            // the D-pad - never about R3 - with no Update call and no time passing.
            // The menu must still open, and A must still be taken from the game.
            Sdl.SetVirtualButton(joystick, ButtonRightStick, 1);
            hook.Capture.PublishContext(
                ControllerNavigationDomain.Field, true, true, false, DateTime.UtcNow, identity: 500);
            Equal((byte)0, Sdl.GetButton(controller, ButtonA),
                "a read of A opens the menu from an R3 the game never asked about");
            Equal(true, hook.Capture.IsOpen, "the menu is open");
            Equal((byte)0, Sdl.GetButton(controller, ButtonDPadDown),
                "and the D-pad is taken from the game too");

            // Release R3, then press A: the selection press must not reach the game.
            Sdl.SetVirtualButton(joystick, ButtonRightStick, 0);
            _ = Sdl.GetButton(controller, ButtonDPadDown);
            Sdl.SetVirtualButton(joystick, ButtonA, 1);
            hook.Capture.PublishContext(
                ControllerNavigationDomain.Field, true, true, false, DateTime.UtcNow, identity: 500);
            Equal((byte)0, Sdl.GetButton(controller, ButtonA),
                "the A that chooses a destination does not reach the game");

            // Held after the close, until released - the tail.
            Equal((byte)0, Sdl.GetButton(controller, ButtonA), "and is still held while down");
            Sdl.SetVirtualButton(joystick, ButtonA, 0);
            _ = Sdl.GetButton(controller, ButtonA);
            Sdl.SetVirtualButton(joystick, ButtonA, 1);
            Equal((byte)1, Sdl.GetButton(controller, ButtonA),
                "and the game gets A back once it has been released and pressed afresh");
            Sdl.SetVirtualButton(joystick, ButtonA, 0);
            _ = Sdl.GetButton(controller, ButtonA);

            // A second device, which is the reported fault. Two pads open means two
            // pads read every frame, and the menu used to belong for ever to whichever
            // one SDL happened to enumerate first - so the pad in the player's hands
            // could never produce an edge, while its ordinary controls kept working.
            var secondDevice = Sdl.AttachVirtual(TypeGameController, 6, 15, 1);
            if (secondDevice >= 0)
            {
                var second = Sdl.GameControllerOpen(secondDevice);
                if (second != 0)
                {
                    var secondJoystick = Sdl.GameControllerGetJoystick(second);

                    // It arrives with its stick already clicked. The first thing we
                    // ever learn about a device is not a press: a resting state or a
                    // thumb that happened to be down must be released first.
                    Sdl.SetVirtualButton(secondJoystick, ButtonRightStick, 1);
                    hook.Capture.PublishContext(
                        ControllerNavigationDomain.Field, true, true, false, DateTime.UtcNow, identity: 500);
                    Equal((byte)1, Sdl.GetButton(second, ButtonRightStick),
                        "a click we never saw go down reaches the game untouched");
                    Equal(false, hook.Capture.IsOpen, "and does not open the menu");
                    Equal(controller, hook.LatchedController,
                        "nor move the menu to that pad");

                    // Released and clicked afresh. The game still only asks about A -
                    // it never asks about the stick click - and the menu has to open on
                    // this pad anyway.
                    Sdl.SetVirtualButton(secondJoystick, ButtonRightStick, 0);
                    _ = Sdl.GetButton(second, ButtonA);
                    Sdl.SetVirtualButton(secondJoystick, ButtonRightStick, 1);
                    hook.Capture.PublishContext(
                        ControllerNavigationDomain.Field, true, true, false, DateTime.UtcNow, identity: 500);
                    Equal((byte)0, Sdl.GetButton(second, ButtonA),
                        "a read of A on the second pad opens the menu from its own R3");
                    Equal(true, hook.Capture.IsOpen, "the menu is open");
                    Equal(second, hook.LatchedController, "on the pad that asked for it");

                    // And the pad that used to own it cannot take it back while it is
                    // open: a list somebody is reading is not another player's to grab.
                    Sdl.SetVirtualButton(joystick, ButtonRightStick, 1);
                    hook.Capture.PublishContext(
                        ControllerNavigationDomain.Field, true, true, false, DateTime.UtcNow, identity: 500);
                    Equal((byte)1, Sdl.GetButton(controller, ButtonRightStick),
                        "the other pad keeps its own buttons while somebody else has the menu");
                    Equal(true, hook.Capture.IsOpen, "which stays open");
                    Equal(second, hook.LatchedController, "on the pad that opened it");
                    Sdl.SetVirtualButton(joystick, ButtonRightStick, 0);
                    _ = Sdl.GetButton(controller, ButtonA);

                    // Closing the pad that owns the menu takes the menu with it.
                    Sdl.GameControllerClose(second);
                    Equal((nint)0, hook.LatchedController,
                        "closing the pad that owns the menu releases it");
                    Equal(false, hook.Capture.IsOpen, "and the menu goes with it");
                }

                Sdl.DetachVirtual(secondDevice);
            }

            // With nobody owning it, the pad still plugged in takes the menu back on
            // its very next read - the same first-read latch a lone pad has always had.
            hook.Capture.PublishContext(
                ControllerNavigationDomain.Field, true, true, false, DateTime.UtcNow, identity: 500);
            _ = Sdl.GetButton(controller, ButtonA);
            Equal(controller, hook.LatchedController,
                "and the remaining pad is latched again on its next read");

            // Closing the latched device releases it, through the production Close
            // detour, and nothing is read afterwards.
            var pollsAtClose = hook.Capture.ObservedPolls;
            Sdl.GameControllerClose(controller);
            controller = 0;
            Equal((nint)0, hook.LatchedController, "closing the device releases the latch");
            Equal(false, hook.Capture.IsOpen, "and the menu goes with it");
            Equal(true, hook.Capture.ObservedPolls > pollsAtClose,
                "the close is reported to the policy as a disconnect");
        }
        finally
        {
            if (controller != 0)
            {
                Sdl.GameControllerClose(controller);
            }

            if (device >= 0)
            {
                Sdl.DetachVirtual(device);
            }

            hook.Dispose();
        }

        Equal(false, hook.IsInstalled, "and the hook reports itself gone once disposed");
        Equal((byte)0, Sdl.GetButton(0, ButtonA), "SDL still answers with the detour removed");
    }

    private static class Sdl
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, EntryPoint = "LoadLibraryA")]
        internal static extern nint LoadLibrary(string fileName);

        [DllImport("SDL2.dll", EntryPoint = "SDL_InitSubSystem", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int InitSubSystem(uint flags);

        [DllImport("SDL2.dll", EntryPoint = "SDL_QuitSubSystem", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void QuitSubSystem(uint flags);

        [DllImport("SDL2.dll", EntryPoint = "SDL_GetError", CallingConvention = CallingConvention.Cdecl)]
        private static extern nint GetError();

        [DllImport("SDL2.dll", EntryPoint = "SDL_JoystickAttachVirtual", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int AttachVirtual(int type, int axes, int buttons, int hats);

        [DllImport("SDL2.dll", EntryPoint = "SDL_JoystickDetachVirtual", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int DetachVirtual(int deviceIndex);

        [DllImport("SDL2.dll", EntryPoint = "SDL_JoystickSetVirtualButton", CallingConvention = CallingConvention.Cdecl)]
        private static extern int SetVirtualButtonRaw(nint joystick, int button, byte value);

        /// <summary>
        /// Deliberately <c>SDL_JoystickUpdate</c> and not <c>SDL_GameControllerUpdate</c>.
        /// A game refreshing this way - or through SDL_PumpEvents, which calls it - is
        /// precisely the case the capture must not depend on a Update hook to see.
        /// </summary>
        [DllImport("SDL2.dll", EntryPoint = "SDL_JoystickUpdate", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void JoystickUpdate();

        internal static int SetVirtualButton(nint joystick, int button, byte value)
        {
            var result = SetVirtualButtonRaw(joystick, button, value);
            JoystickUpdate();
            return result;
        }

        [DllImport("SDL2.dll", EntryPoint = "SDL_GameControllerOpen", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint GameControllerOpen(int joystickIndex);

        [DllImport("SDL2.dll", EntryPoint = "SDL_GameControllerClose", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void GameControllerClose(nint controller);

        [DllImport("SDL2.dll", EntryPoint = "SDL_GameControllerGetJoystick", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint GameControllerGetJoystick(nint controller);

        /// <summary>
        /// The caller's side of the contract, declared here rather than reused from the
        /// hook: this is how the game reaches the export, so it is how the test must.
        /// </summary>
        [DllImport("SDL2.dll", EntryPoint = "SDL_GameControllerGetButton", CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte GetButton(nint controller, int button);

        internal static string GetErrorText()
        {
            var text = GetError();
            return text == 0 ? "no SDL error" : Marshal.PtrToStringAnsi(text) ?? "no SDL error";
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"SDL controller capture: {message}. Expected {expected}, actual {actual}.");
        }
    }
}
