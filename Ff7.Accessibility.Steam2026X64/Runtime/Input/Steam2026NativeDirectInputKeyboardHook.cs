using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Ff7.Accessibility.LegacyLayout;
using Reloaded.Hooks.Definitions;

namespace Ff7.Accessibility.Steam2026X64.Runtime.Input;

/// <summary>
/// Marks automatic walking's direction in the legacy keyboard state the Steam 2026 host
/// hands the translated game, inside the call that builds it.
///
/// <para><b>The seam.</b> The host implements DirectInput itself. Its native function
/// table registers <c>"IDirectInputDeviceA::GetDeviceState"</c> against
/// <see cref="GetDeviceStateRva"/>, and for a 256-byte request that function clears a
/// scratch buffer, sets <c>0x80</c> for each of its own logical actions through a constant
/// token table - actions 10..13 being up/down/left/right as <c>0x48/0x50/0x4B/0x4D</c> -
/// adds the analogue directions, and copies all 256 bytes into the guest address the
/// caller asked for. Translated <c>FUN_0041F55E</c> is that caller, and it always asks for
/// <see cref="ExpectedGuestDestination"/>; <c>FUN_0041A21E</c> then turns those bytes into
/// the held direction mask through the control banks at <c>0x009A85E8</c>.</para>
///
/// <para><b>Why here and not a timer.</b> The buffer is rebuilt from scratch on every
/// poll, so a write from outside would land in an undefined phase. Running after the
/// original inside the same call is the only place where "after it is filled, before the
/// game reads it" is guaranteed rather than hoped for.</para>
///
/// <para><b>What it is allowed to do.</b> One original call, its <c>HRESULT</c> returned
/// untouched, and - only on a successful 256-byte call to the expected destination - the
/// high bit set on the tokens the control table itself names for the directions being
/// driven. Bytes are only ever ORed, never cleared, so a key the player is physically
/// holding survives; and because the original rebuilds the buffer, letting go needs no
/// write at all.</para>
///
/// <para>Installation requires the exact supported image fingerprint, the registration
/// record still naming this function, and the live entry prefix. The live prefix is the
/// only usable signature: this image's code is encrypted on disk.</para>
/// </summary>
internal sealed class Steam2026NativeDirectInputKeyboardHook : IDisposable
{
    /// <summary>The native <c>GetDeviceState</c> implementation.</summary>
    internal const ulong GetDeviceStateRva = 0x015577E0;

    /// <summary>
    /// Its entry in the host's named native-function table: name pointer, function
    /// pointer, then an argument word. <c>Acquire</c> sits at <c>-0x20</c> with one
    /// argument, which is what makes the three here a cross-check on the ABI rather than
    /// an assumption.
    /// </summary>
    internal const ulong RegistrationRecordRva = 0x016D2B38;

    internal const int RegistrationRecordSize = 0x18;

    internal const ulong NameRva = 0x01648720;

    internal const string ExpectedName = "IDirectInputDeviceA::GetDeviceState";

    /// <summary>The argument word of the registration record: three parameters.</summary>
    internal const ulong ExpectedArgumentWord = 0x0000000100000003;

    internal const string ExpectedPrefixHex = "4053574883EC588BDA4585C0740D418BC8";

    /// <summary>A full legacy keyboard state. Any other size is a different device.</summary>
    internal const int KeyboardStateLength = 0x100;

    /// <summary>
    /// The guest buffer translated <c>FUN_0041F55E</c> passes, and the only destination
    /// this will write to. Four-byte aligned, so the aligned word holding any of the 256
    /// indices is wholly inside the buffer the callback owns.
    /// </summary>
    internal const uint ExpectedGuestDestination = 0x009ADAE4;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int GetDeviceStateDelegate(nint device, int byteCount, uint guestDestination);

    // Rooted for the life of the hook, including one the backend failed to remove: a
    // collected delegate is a jump into freed memory on the game's input path.
    private readonly GetDeviceStateDelegate detour;
    private readonly GetDeviceStateDelegate? injectedOriginal;
    private readonly IHook<GetDeviceStateDelegate>? hook;
    private readonly Steam2026NativeDirectionalInputSink sink;
    private readonly ILegacyAddressSpace addressSpace;
    private readonly ILegacyMemoryWriter writer;
    private readonly Func<DateTime> now;
    private readonly Action<string>? log;
    private long appliedOverlays;
    private long refusedDestinations;
    private long failedWrites;
    private int loggedFailure;
    private int disposed;

    private Steam2026NativeDirectInputKeyboardHook(
        Steam2026NativeDirectionalInputSink sink,
        TranslatedX86AddressSpace addressSpace,
        IReloadedHooks hooks,
        ulong hostAddress,
        Func<DateTime>? now,
        Action<string>? log)
    {
        this.sink = sink;
        this.addressSpace = addressSpace;
        writer = addressSpace;
        this.now = now ?? (static () => DateTime.UtcNow);
        this.log = log;
        detour = OnGetDeviceState;
        try
        {
            hook = hooks.CreateHook(detour, checked((long)hostAddress), -1);
            hook.Activate();
        }
        catch
        {
            DisableHook();
            throw;
        }
    }

    /// <summary>
    /// The overlay path over injected memory, an injected original and an injected clock.
    /// Everything the callback decides is deterministic and belongs in an ordinary test;
    /// installing over the real native entry is proved separately.
    /// </summary>
    private Steam2026NativeDirectInputKeyboardHook(
        Steam2026NativeDirectionalInputSink sink,
        ILegacyAddressSpace addressSpace,
        ILegacyMemoryWriter writer,
        GetDeviceStateDelegate injectedOriginal,
        Func<DateTime> now,
        Action<string>? log)
    {
        this.sink = sink;
        this.addressSpace = addressSpace;
        this.writer = writer;
        this.injectedOriginal = injectedOriginal;
        this.now = now;
        this.log = log;
        detour = OnGetDeviceState;
    }

    internal bool IsInstalled =>
        Volatile.Read(ref disposed) == 0 &&
        (hook is { IsHookActivated: true, IsHookEnabled: true }
            || (hook is null && injectedOriginal is not null));

    /// <summary>Directional overlays written into the game's buffer. Diagnostic only.</summary>
    internal long AppliedOverlays => Interlocked.Read(ref appliedOverlays);

    /// <summary>
    /// 256-byte calls whose destination was not the one the translated caller uses. Zero
    /// on the supported host; anything else is worth knowing before it is trusted.
    /// </summary>
    internal long RefusedDestinations => Interlocked.Read(ref refusedDestinations);

    internal long FailedWrites => Interlocked.Read(ref failedWrites);

    internal static bool TryInstall(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        INativeMemoryWriter? writer,
        IReloadedHooks hooks,
        Steam2026NativeDirectionalInputSink sink,
        out Steam2026NativeDirectInputKeyboardHook installed,
        out string diagnostic,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(hooks);
        ArgumentNullException.ThrowIfNull(sink);
        installed = null!;
        if (writer is null)
        {
            diagnostic =
                "The Steam 2026 host does not support the memory writer the keyboard-state " +
                "overlay needs.";
            return false;
        }

        try
        {
            if (!TryValidateRegistration(
                    moduleBase,
                    moduleImageSize,
                    memory,
                    out var hostAddress,
                    out diagnostic))
            {
                return false;
            }

            // Validates the exact supported fingerprint and the guest address resolver -
            // the same resolver the shim itself calls to turn the destination into a host
            // pointer, so agreeing with it is the point.
            var addressSpace = ValidatedTranslatedX86AddressSpaceFactory.Create(
                fingerprint,
                moduleBase,
                memory,
                writer);
            var candidate = new Steam2026NativeDirectInputKeyboardHook(
                sink,
                addressSpace,
                hooks,
                hostAddress,
                now: null,
                log: log);
            installed = candidate;
            sink.AttachOverlay(() => candidate.IsInstalled);
            diagnostic =
                "Automatic movement is delivered through the native " +
                $"IDirectInputDeviceA::GetDeviceState at +0x{GetDeviceStateRva:X}.";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic = $"The native keyboard-state overlay could not be installed: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Revalidates that the named registration record still points at the expected
    /// function, that the name it is registered under is still that name, that its
    /// argument count still matches the ABI this detour declares, and that the live entry
    /// prefix is the one that was decompiled.
    /// </summary>
    internal static bool TryValidateRegistration(
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        out ulong hostAddress,
        out string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(memory);
        hostAddress = 0;
        diagnostic = string.Empty;
        if (moduleBase == 0 || moduleImageSize == 0
            || moduleBase > ulong.MaxValue - moduleImageSize)
        {
            diagnostic = "The Steam 2026 module bounds are unusable.";
            return false;
        }

        var imageEndExclusive = moduleBase + moduleImageSize;
        var recordAddress = moduleBase + RegistrationRecordRva;
        var expectedName = moduleBase + NameRva;
        var expectedFunction = moduleBase + GetDeviceStateRva;
        var prefix = Convert.FromHexString(ExpectedPrefixHex);
        if (recordAddress + RegistrationRecordSize > imageEndExclusive
            || expectedName + (ulong)ExpectedName.Length + 1 > imageEndExclusive
            || expectedFunction + (ulong)prefix.Length > imageEndExclusive)
        {
            diagnostic = "The native DirectInput registration is outside the Steam 2026 image.";
            return false;
        }

        Span<byte> record = stackalloc byte[RegistrationRecordSize];
        Span<byte> confirmation = stackalloc byte[RegistrationRecordSize];
        if (!memory.TryRead(recordAddress, record)
            || !memory.TryRead(recordAddress, confirmation)
            || !record.SequenceEqual(confirmation))
        {
            diagnostic = "The native DirectInput registration record is unreadable or unstable.";
            return false;
        }

        if (BinaryPrimitives.ReadUInt64LittleEndian(record) != expectedName
            || BinaryPrimitives.ReadUInt64LittleEndian(record[sizeof(ulong)..]) != expectedFunction
            || BinaryPrimitives.ReadUInt64LittleEndian(record[(2 * sizeof(ulong))..])
                != ExpectedArgumentWord)
        {
            diagnostic =
                "The native DirectInput registration no longer names GetDeviceState at the " +
                "expected address with three arguments.";
            return false;
        }

        Span<byte> name = stackalloc byte[ExpectedName.Length + 1];
        if (!memory.TryRead(expectedName, name)
            || name[^1] != 0
            || !Encoding.ASCII.GetString(name[..^1]).Equals(ExpectedName, StringComparison.Ordinal))
        {
            diagnostic = "The native DirectInput registration name does not read as expected.";
            return false;
        }

        Span<byte> actualPrefix = stackalloc byte[prefix.Length];
        Span<byte> actualPrefixAgain = stackalloc byte[prefix.Length];
        if (!memory.TryRead(expectedFunction, actualPrefix)
            || !memory.TryRead(expectedFunction, actualPrefixAgain)
            || !actualPrefix.SequenceEqual(actualPrefixAgain)
            || !actualPrefix.SequenceEqual(prefix))
        {
            diagnostic =
                "The live native GetDeviceState prefix is not the one this overlay was " +
                "written against.";
            return false;
        }

        if (!memory.TryQueryRegion(expectedFunction, out var region)
            || !region.IsCommitted
            || !region.IsExecutable
            || region.AllocationBase != moduleBase
            || region.Size == 0
            || expectedFunction < region.BaseAddress
            || expectedFunction + (ulong)prefix.Length > region.BaseAddress + region.Size)
        {
            diagnostic =
                "The native GetDeviceState entry is not committed executable code inside the " +
                "Steam 2026 image.";
            return false;
        }

        hostAddress = expectedFunction;
        return true;
    }

    internal static Steam2026NativeDirectInputKeyboardHook CreateForOverlayTest(
        Steam2026NativeDirectionalInputSink sink,
        ILegacyAddressSpace addressSpace,
        ILegacyMemoryWriter writer,
        GetDeviceStateDelegate injectedOriginal,
        Func<DateTime> now,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(addressSpace);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(injectedOriginal);
        ArgumentNullException.ThrowIfNull(now);
        var created = new Steam2026NativeDirectInputKeyboardHook(
            sink, addressSpace, writer, injectedOriginal, now, log);
        sink.AttachOverlay(() => created.IsInstalled);
        return created;
    }

    /// <summary>
    /// Installs the production detour over an already-validated native address. Used by
    /// the isolated native host to hook a real function with the real backend; production
    /// always goes through <see cref="TryInstall"/>, which validates the identity first.
    /// </summary>
    internal static Steam2026NativeDirectInputKeyboardHook CreateOverValidatedAddress(
        Steam2026NativeDirectionalInputSink sink,
        TranslatedX86AddressSpace addressSpace,
        IReloadedHooks hooks,
        ulong hostAddress,
        Func<DateTime>? now = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(addressSpace);
        ArgumentNullException.ThrowIfNull(hooks);
        if (hostAddress == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hostAddress));
        }

        var created = new Steam2026NativeDirectInputKeyboardHook(
            sink, addressSpace, hooks, hostAddress, now, log);
        sink.AttachOverlay(() => created.IsInstalled);
        return created;
    }

    /// <summary>One host keyboard poll, as the game would make it. Test seam.</summary>
    internal int InvokeForTest(nint device, int byteCount, uint guestDestination) =>
        OnGetDeviceState(device, byteCount, guestDestination);

    private int OnGetDeviceState(nint device, int byteCount, uint guestDestination)
    {
        var result = CallOriginal(device, byteCount, guestDestination);
        if (Volatile.Read(ref disposed) != 0
            || result != 0
            || byteCount != KeyboardStateLength)
        {
            // A failed call, or a different device's state: nothing of ours belongs in it.
            return result;
        }

        try
        {
            Overlay(guestDestination);
        }
        catch (Exception ex)
        {
            // Never propagate into the host's input path. Logged once so a persistent
            // fault is visible without filling the log sixty times a second.
            if (Interlocked.Exchange(ref loggedFailure, 1) == 0)
            {
                log?.Invoke($"Automatic movement could not reach the game's keyboard state: {ex.Message}");
            }
        }

        return result;
    }

    private int CallOriginal(nint device, int byteCount, uint guestDestination) =>
        injectedOriginal is not null
            ? injectedOriginal(device, byteCount, guestDestination)
            : hook!.OriginalFunction(device, byteCount, guestDestination);

    private void Overlay(uint guestDestination)
    {
        if (guestDestination != ExpectedGuestDestination)
        {
            Interlocked.Increment(ref refusedDestinations);
            return;
        }

        Span<byte> tokens = stackalloc byte[Steam2026NativeDirectionalInputSink.MaxHeldTokens];
        if (!sink.TryTakeDesired(now(), tokens, out var count))
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            // The DIK array is one byte per key and the writer is atomic in four, so the
            // aligned word holding the token is read, ORed and exchanged. The destination
            // is four-byte aligned and 256 bytes long, so that word never leaves the
            // buffer the callback owns, and this runs on the thread that has just filled
            // it - no other writer is in it.
            var address = guestDestination + tokens[index];
            var wordAddress = address & ~3u;
            if (!addressSpace.TryReadUInt32(wordAddress, out var word))
            {
                Interlocked.Increment(ref failedWrites);
                continue;
            }

            var updated = word | (0x80u << (int)((address & 3) * 8));
            if (updated == word)
            {
                // Already held, by the player or by the host's own action mapping.
                continue;
            }

            if (!writer.TryWriteInt32(wordAddress, unchecked((int)updated)))
            {
                Interlocked.Increment(ref failedWrites);
                continue;
            }

            Interlocked.Increment(ref appliedOverlays);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        // Detour out first, then forget the direction, so a call already in flight cannot
        // find a torn-down sink.
        DisableHook();
        sink.Clear();
    }

    private void DisableHook()
    {
        try
        {
            if (hook is { IsHookActivated: true, IsHookEnabled: true })
            {
                hook.Disable();
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"The native keyboard-state detour could not be removed: {ex.Message}");
        }
    }
}
