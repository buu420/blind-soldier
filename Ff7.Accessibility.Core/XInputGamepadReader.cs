using System.Runtime.InteropServices;

namespace Ff7.Accessibility.Core;

/// <summary>
/// Reads one controller through XInput's documented <c>XInputGetState</c>.
///
/// <para>Nothing here is guessed. The struct layout, the slot count, the
/// <c>ERROR_DEVICE_NOT_CONNECTED</c> return and the wButtons bit values are the
/// published contract, and the three DLL names are the three shipped versions in
/// declining order of availability. No addresses and no offsets into the game are
/// involved: this asks Windows what the pad is doing.</para>
///
/// <para>The slot is latched. Four slots exist and a second pad - a charging
/// controller, a racing wheel, a headset adapter - can appear at any time; if the
/// reader drifted to it the player would press a button and nothing would happen,
/// with no way to tell why. The latch is only given up when the latched slot itself
/// reports no device.</para>
/// </summary>
public sealed class XInputGamepadReader : IGamepadReader
{
    private const int MaximumUsers = 4;
    private const int ErrorSuccess = 0;
    private const int ErrorDeviceNotConnected = 1167;

    private readonly Func<int, (int Result, uint PacketNumber, ushort Buttons)> getState;
    private readonly Action<string>? log;

    private int activeUserIndex = -1;

    internal XInputGamepadReader(
        Func<int, (int Result, uint PacketNumber, ushort Buttons)> getState,
        Action<string>? log = null)
    {
        this.getState = getState ?? throw new ArgumentNullException(nameof(getState));
        this.log = log;
    }

    /// <summary>
    /// The reader for this machine, or null when no XInput runtime answers at all.
    /// A null is not a failure worth reporting loudly: plenty of players have no
    /// controller, and the keyboard bindings are unchanged.
    /// </summary>
    public static XInputGamepadReader? TryCreateCurrentProcess(Action<string>? log = null)
    {
        foreach (var probe in NativeMethods.Probes)
        {
            try
            {
                // A successful call or an honest "nothing in slot 0" both prove the
                // export is there. Only a failure to bind is a reason to try the
                // next DLL.
                var result = probe(0, out _);
                if (result is ErrorSuccess or ErrorDeviceNotConnected)
                {
                    return new XInputGamepadReader(
                        userIndex =>
                        {
                            var code = probe(userIndex, out var state);
                            return (code, state.PacketNumber, state.Gamepad.Buttons);
                        },
                        log);
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
            catch (BadImageFormatException)
            {
            }
        }

        log?.Invoke("No XInput runtime answered; controller navigation is unavailable.");
        return null;
    }

    public int ActiveUserIndex => activeUserIndex;

    public GamepadSnapshot Poll()
    {
        if (activeUserIndex >= 0 && TryPoll(activeUserIndex, out var latched))
        {
            return latched;
        }

        if (activeUserIndex >= 0)
        {
            log?.Invoke($"Controller in slot {activeUserIndex} disconnected.");
            activeUserIndex = -1;
        }

        for (var userIndex = 0; userIndex < MaximumUsers; userIndex++)
        {
            if (!TryPoll(userIndex, out var found))
            {
                continue;
            }

            activeUserIndex = userIndex;
            log?.Invoke($"Controller navigation is listening to slot {userIndex}.");
            return found;
        }

        return GamepadSnapshot.Disconnected;
    }

    private bool TryPoll(int userIndex, out GamepadSnapshot snapshot)
    {
        snapshot = GamepadSnapshot.Disconnected;
        int result;
        uint packetNumber;
        ushort buttons;
        try
        {
            (result, packetNumber, buttons) = getState(userIndex);
        }
        catch (Exception ex)
        {
            log?.Invoke($"XInput read failed for slot {userIndex}: {ex.Message}");
            return false;
        }

        if (result != ErrorSuccess)
        {
            return false;
        }

        snapshot = new GamepadSnapshot(true, userIndex, packetNumber, (GamepadButton)buttons);
        return true;
    }

    private static class NativeMethods
    {
        internal delegate int GetStateProbe(int userIndex, out XInputState state);

        internal static readonly GetStateProbe[] Probes =
        [
            XInputGetState14,
            XInputGetState13,
            XInputGetState910,
        ];

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int XInputGetState14(int userIndex, out XInputState state);

        [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int XInputGetState13(int userIndex, out XInputState state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int XInputGetState910(int userIndex, out XInputState state);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XInputGamepad
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
    internal struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }
}
