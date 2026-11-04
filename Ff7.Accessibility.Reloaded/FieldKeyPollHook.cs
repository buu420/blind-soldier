using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The legacy field loop's key masks: FUN_006499F7's current, previous, pressed and released
/// words at 0x00CC0DF0 to 0x00CC0DFC and again at 0x00CC0E00 to 0x00CC0E0C, all in the game's
/// own memory.
/// </summary>
internal interface IFieldKeyGlobals
{
    uint Read(uint address);

    void Write(uint address, uint value);
}

/// <summary>
/// Ends auto walk's hold near a Mutant Flytrap inside the legacy game's own field loop.
///
/// <para>FUN_0063C17F reads the field's keys once per iteration, calling FUN_006499F7 at
/// 0x0063C28B - its only caller, which pushes two arguments the function never reads - and passes
/// the mask that call returns, from the same local, to FUN_006342C6 at 0x0063C75C, which walks
/// the leader by its direction bits (0x1000 to 0x8000) and its run bit 0x40. FFNx's 60 fps swaps
/// that second call for one that passes the same mask on, and leaves the first call and
/// FUN_006499F7 alone. Field scripts read the same keys from the globals FUN_006499F7 writes:
/// IFKEY (0061220D) the current words, IFKEYON (0061225D) the pressed and IFKEYOFF (006122B0) the
/// released, through the field object at 0x00CC0D88.</para>
///
/// <para>So once the <see cref="AutoWalkPollLease"/> is spent, the detour returns the mask with
/// the directions taken out and presents that same mask to the scripts: the current words hold
/// it, and the pressed and released words are derived from it against the previous words exactly
/// as FUN_006499F7 derives them, every other button untouched. The leader stops, and no script
/// sees the held key, on the game's next iteration, at any frame rate or speed-up, while the
/// SendInput keys are still down and whether or not the monitor that pressed them ever comes
/// back. Unfiltered - disarmed, or inside the lease - nothing is written.</para>
///
/// <para>Installed only over the code that was decompiled: FUN_006499F7's whole body, which
/// fixes every one of those addresses and formulas, the call and the hand-off are checked byte
/// for byte first. Another exe, or a hook already there, leaves it out, and auto walk then hands
/// the jaws' fields to the player (<see cref="AutoWalkPollLease.ResolveFieldHandoff"/>).</para>
/// </summary>
internal sealed class FieldKeyPollHook : IDisposable
{
    /// <summary>FUN_006499F7: the field's keys as one mask, kept at 0x00CC0DF0 and 0x00CC0E00 with their edges, and returned.</summary>
    internal const uint KeyPollAddress = 0x006499F7;

    /// <summary>FUN_0063C17F's call to it, the only one in the image.</summary>
    internal const uint KeyPollCallSite = 0x0063C28B;

    /// <summary>FUN_0063C17F handing that mask to FUN_006342C6, that function's only call.</summary>
    internal const uint MovementArgumentSite = 0x0063C758;

    /// <summary>
    /// FUN_006499F7 from its entry to its ret at 0x00649B28: the device flags gathered into one
    /// mask, then each word pair written as current, previous, pressed and released.
    /// </summary>
    internal const string ExpectedBodyHex =
        "558BEC51E876FEFFFFA1A811D000C1E00F8B0DB011D000C1E10E0BC18B15B411D000C1E20D0BC28B0DAC11D0" +
        "00C1E10C0BC18B15DC11D000C1E20B0BC28B0DE011D000C1E1080BC18B15BC11D000C1E2070BC28B0DC811D0" +
        "00C1E1060BC18B15C011D000C1E2050BC28B0DC411D000C1E1040BC18B15CC11D000C1E2030BC28B0DD011D0" +
        "00D1E10BC18B15D411D000C1E2020BC20B05D811D0008945FCA1F00DCC00A3F40DCC008B4DFC890DF00DCC00" +
        "8B15F00DCC003315F40DCC002315F00DCC008915F80DCC00A1F00DCC003305F40DCC008B0DF00DCC00F7D123" +
        "C1A3FC0DCC008B15000ECC008915040ECC008B45FCA3000ECC008B0D000ECC00330D040ECC00230D000ECC00" +
        "890D080ECC008B15000ECC003315040ECC00A1000ECC00F7D023D089150C0ECC00A1000ECC008BE55DC3";

    /// <summary>call FUN_006499F7; add esp,8 - the caller's cleanup; mov [ebp-0x18],eax.</summary>
    internal const string ExpectedPollCallHex = "E867D7000083C4088945E8";

    /// <summary>mov eax,[ebp-0x18]; push eax; call - the target is FUN_006342C6, or FFNx's in its place.</summary>
    internal const string ExpectedMovementArgumentHex = "8B45E850E8";

    internal const uint CurrentKeys = 0x00CC0DF0;
    internal const uint PreviousKeys = 0x00CC0DF4;
    internal const uint PressedKeys = 0x00CC0DF8;
    internal const uint ReleasedKeys = 0x00CC0DFC;
    internal const uint CurrentKeysAgain = 0x00CC0E00;
    internal const uint PreviousKeysAgain = 0x00CC0E04;
    internal const uint PressedKeysAgain = 0x00CC0E08;
    internal const uint ReleasedKeysAgain = 0x00CC0E0C;

    [global::Reloaded.Hooks.Definitions.X86.Function(global::Reloaded.Hooks.Definitions.X86.CallingConventions.Cdecl)]
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint FieldKeyPollDelegate(int unusedFirst, int unusedSecond);

    private readonly AutoWalkPollLease lease;
    private readonly IFieldKeyGlobals globals;

    // Rooted for the life of this object, which its owner keeps even when activation fails: a
    // collected delegate is a jump into freed memory on the game's own thread.
    private readonly FieldKeyPollDelegate detour;
    private readonly IHook<FieldKeyPollDelegate> hook;
    private int disposed;

    // Created, never activated, here: activation is the caller's, once this is rooted.
    private FieldKeyPollHook(IReloadedHooks hooks, AutoWalkPollLease lease, IFieldKeyGlobals globals)
    {
        this.lease = lease;
        this.globals = globals;
        detour = OnFieldKeyPoll;
        hook = hooks.CreateHook(detour, KeyPollAddress, -1);
    }

    internal bool IsInstalled =>
        Volatile.Read(ref disposed) == 0 && hook is { IsHookActivated: true, IsHookEnabled: true };

    /// <param name="installed">
    /// The hook once it was created, whether or not it went live: its owner keeps it, and
    /// disposes of it, either way.
    /// </param>
    internal static bool TryInstall(
        IReloadedHooks hooks,
        ILegacyAddressSpace code,
        AutoWalkPollLease lease,
        IFieldKeyGlobals globals,
        out FieldKeyPollHook? installed,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(globals);
        installed = null;
        if (!IsDecompiledCode(code, out diagnostic))
        {
            return false;
        }

        FieldKeyPollHook created;
        try
        {
            created = new FieldKeyPollHook(hooks, lease, globals);
        }
        catch (Exception ex)
        {
            diagnostic = $"The field key read hook could not be created: {ex.Message}";
            return false;
        }

        installed = created;
        try
        {
            created.hook.Activate();
        }
        catch (Exception ex)
        {
            created.Dispose();
            diagnostic = $"The field key read hook could not be activated: {ex.Message}";
            return false;
        }

        lease.AttachPoller(() => created.IsInstalled);
        diagnostic =
            $"Auto walk's hold near the Mutant Flytraps ends on the field loop's own key read at 0x{KeyPollAddress:X8}.";
        return true;
    }

    /// <summary>FUN_006499F7's body, the call and the hand-off as decompiled, or why not.</summary>
    internal static bool IsDecompiledCode(ILegacyAddressSpace code, out string diagnostic)
    {
        foreach (var (address, expected, what) in new[]
                 {
                     (KeyPollAddress, ExpectedBodyHex, "FUN_006499F7"),
                     (KeyPollCallSite, ExpectedPollCallHex, "the field loop's key read"),
                     (MovementArgumentSite, ExpectedMovementArgumentHex, "the field loop's hand-off to the leader's movement")
                 })
        {
            var bytes = Convert.FromHexString(expected);
            var actual = new byte[bytes.Length];
            if (!code.TryRead(address, actual) || !actual.AsSpan().SequenceEqual(bytes))
            {
                diagnostic = $"The field key read hook was not installed: {what} at 0x{address:X8} is not the decompiled code.";
                return false;
            }
        }

        diagnostic = string.Empty;
        return true;
    }

    /// <summary>
    /// One field key read, after FUN_006499F7 has run: the mask the leader moves by, and, when
    /// the lease takes directions out of it, the same mask presented to the field scripts.
    /// </summary>
    internal static uint Deliver(uint keys, AutoWalkPollLease lease, IFieldKeyGlobals globals)
    {
        var filtered = lease.FilterFieldKeys(keys);
        if (filtered != keys)
        {
            Present(filtered, globals, CurrentKeys, PreviousKeys, PressedKeys, ReleasedKeys);
            Present(filtered, globals, CurrentKeysAgain, PreviousKeysAgain, PressedKeysAgain, ReleasedKeysAgain);
        }

        return filtered;
    }

    // FUN_006499F7 has already moved the old current word into the previous one; the rest is its
    // own derivation over the filtered mask.
    private static void Present(uint keys, IFieldKeyGlobals globals, uint current, uint previous, uint pressed, uint released)
    {
        var before = globals.Read(previous);
        globals.Write(current, keys);
        globals.Write(pressed, (keys ^ before) & keys);
        globals.Write(released, (keys ^ before) & ~keys);
    }

    private uint OnFieldKeyPoll(int unusedFirst, int unusedSecond) =>
        Deliver(hook.OriginalFunction(unusedFirst, unusedSecond), lease, globals);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (hook is { IsHookActivated: true, IsHookEnabled: true })
            {
                hook.Disable();
            }
        }
        catch
        {
            // Still rooted by its owner, so a detour the backend could not remove lands on
            // managed code; the lease reads as unpolled, so its fields go back to the player.
        }
    }
}

/// <summary>The key masks in the running game's own memory: this process, on its own thread.</summary>
internal sealed class NativeFieldKeyGlobals : IFieldKeyGlobals
{
    public unsafe uint Read(uint address) => *(uint*)address;

    public unsafe void Write(uint address, uint value) => *(uint*)address = value;
}
