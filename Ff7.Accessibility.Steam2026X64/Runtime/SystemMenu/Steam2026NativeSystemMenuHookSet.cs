using Reloaded.Hooks.Definitions;
using System.Runtime.InteropServices;

namespace Ff7.Accessibility.Steam2026X64.Runtime.SystemMenu;

/// <summary>
/// Hooks the per-frame native MUI settings-manager entry point used by the
/// Steam 2026 Escape menu. The detour invokes the original exactly once and
/// publishes only the stable manager host pointer; all memory decoding and
/// speech remain on the research worker.
/// </summary>
internal sealed class Steam2026NativeSystemMenuHookSet : IDisposable
{
    private readonly Steam2026NativeSystemMenuManagerTickOriginal managerTickDetour;
    private readonly IHook<Steam2026NativeSystemMenuManagerTickOriginal> managerTickHook;
    private readonly Steam2026NativeSystemMenuDirectionInputOriginal directionInputDetour;
    private readonly IHook<Steam2026NativeSystemMenuDirectionInputOriginal> directionInputHook;
    private readonly Steam2026NativeSystemMenuDirectionInputTracker directionInputTracker = new();
    private long latestManagerHost;
    private int disposed;

    private Steam2026NativeSystemMenuHookSet(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(hooks);
        ValidateFingerprint(fingerprint);
        if (moduleBase == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleBase));
        }

        if (moduleImageSize == 0 || moduleBase > ulong.MaxValue - moduleImageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleImageSize));
        }

        var moduleEndExclusive = moduleBase + moduleImageSize;
        ValidateExecutableTarget(
            "MUI settings-manager tick",
            moduleBase,
            moduleEndExclusive,
            Steam2026NativeSystemMenuDefinitions.ManagerTickRva,
            Steam2026NativeSystemMenuDefinitions.ManagerTickPrefix,
            memory);
        ValidateExecutableTarget(
            "MUI direction-input callback",
            moduleBase,
            moduleEndExclusive,
            Steam2026NativeSystemMenuDefinitions.DirectionInputRva,
            Steam2026NativeSystemMenuDefinitions.DirectionInputPrefix,
            memory);
        foreach (var definition in Steam2026NativeSystemMenuDefinitions.All)
        {
            ValidateVtable(
                definition,
                moduleBase,
                moduleEndExclusive,
                memory);
        }

        managerTickDetour = OnManagerTick;
        managerTickHook = hooks.CreateHook(
            managerTickDetour,
            checked((long)(
                moduleBase
                +
                Steam2026NativeSystemMenuDefinitions.ManagerTickRva)),
            -1);
        directionInputDetour = OnDirectionInput;
        directionInputHook = hooks.CreateHook(
            directionInputDetour,
            checked((long)(
                moduleBase
                +
                Steam2026NativeSystemMenuDefinitions.DirectionInputRva)),
            -1);
        try
        {
            managerTickHook.Activate();
            directionInputHook.Activate();
        }
        catch
        {
            DisableHook(directionInputHook);
            DisableHook(managerTickHook);
            throw;
        }
    }

    internal bool IsFatallyDegraded =>
        Volatile.Read(ref disposed) == 0
        && managerTickHook is not
        {
            IsHookActivated: true,
            IsHookEnabled: true
        }
        || Volatile.Read(ref disposed) == 0
        && directionInputHook is not
        {
            IsHookActivated: true,
            IsHookEnabled: true
        };

    internal static bool TryCreate(
        Steam2026FingerprintResult fingerprint,
        ulong moduleBase,
        ulong moduleImageSize,
        INativeMemoryReader memory,
        IReloadedHooks hooks,
        out Steam2026NativeSystemMenuHookSet hookSet,
        out string diagnostic)
    {
        hookSet = null!;
        try
        {
            hookSet = new Steam2026NativeSystemMenuHookSet(
                fingerprint,
                moduleBase,
                moduleImageSize,
                memory,
                hooks);
            diagnostic =
                "Installed the exact-identity native MUI settings-manager and direction-input callbacks.";
            return true;
        }
        catch (Exception ex)
        {
            diagnostic =
                $"Native Escape-menu MUI manager is not ready: {ex.Message}";
            return false;
        }
    }

    internal bool TryGetLatestManagerHost(out ulong host)
    {
        var captured = Volatile.Read(ref latestManagerHost);
        if (Volatile.Read(ref disposed) != 0 || captured == 0)
        {
            host = 0;
            return false;
        }

        host = unchecked((ulong)captured);
        return true;
    }

    internal bool TryGetVerticalNavigationGeneration(out long generation)
    {
        generation = directionInputTracker.Generation;
        return Volatile.Read(ref disposed) == 0;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        Volatile.Write(ref latestManagerHost, 0);
        DisableHook(directionInputHook);
        DisableHook(managerTickHook);
    }

    private void OnManagerTick(nint host, float elapsedSeconds)
    {
        managerTickHook.OriginalFunction(host, elapsedSeconds);
        try
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                Volatile.Write(ref latestManagerHost, host.ToInt64());
            }
        }
        catch
        {
            // A managed exception must never cross the native MUI callback.
        }
    }

    private void OnDirectionInput(nint callbackContext, nint inputEvent)
    {
        var directionCode = 0;
        try
        {
            if (inputEvent != 0)
            {
                directionCode = Marshal.ReadInt32(inputEvent);
            }
        }
        catch
        {
            // The original callback still owns malformed native input.
        }

        directionInputHook.OriginalFunction(callbackContext, inputEvent);
        try
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                directionInputTracker.Observe(directionCode);
            }
        }
        catch
        {
            // A managed exception must never cross the native MUI callback.
        }
    }

    private static void ValidateFingerprint(Steam2026FingerprintResult fingerprint)
    {
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
            throw new ArgumentException(
                "Native system-menu hooks require the exact supported Steam 2026 x64 fingerprint.",
                nameof(fingerprint));
        }
    }

    private static void ValidateExecutableTarget(
        string role,
        ulong moduleBase,
        ulong moduleEndExclusive,
        ulong rva,
        ReadOnlySpan<byte> expected,
        INativeMemoryReader memory)
    {
        if (!TryAdd(moduleBase, rva, out var address)
            || !IsInsideImage(
                address,
                (ulong)expected.Length,
                moduleBase,
                moduleEndExclusive)
            || !memory.TryQueryRegion(address, out var firstRegion)
            || !IsValidRange(
                firstRegion,
                address,
                (ulong)expected.Length,
                moduleBase,
                executable: true))
        {
            throw new InvalidDataException(
                $"Native system-menu {role} is outside the supported executable image.");
        }

        Span<byte> first = stackalloc byte[expected.Length];
        Span<byte> second = stackalloc byte[expected.Length];
        if (!memory.TryRead(address, first)
            || !memory.TryRead(address, second)
            || !memory.TryQueryRegion(address, out var secondRegion)
            || firstRegion != secondRegion
            || !IsValidRange(
                secondRegion,
                address,
                (ulong)expected.Length,
                moduleBase,
                executable: true)
            || !first.SequenceEqual(second)
            || !first.SequenceEqual(expected))
        {
            throw new InvalidDataException(
                $"Native system-menu {role} bytes do not match the supported executable.");
        }
    }

    private static void ValidateVtable(
        Steam2026NativeSystemMenuDefinition definition,
        ulong moduleBase,
        ulong moduleEndExclusive,
        INativeMemoryReader memory)
    {
        const ulong requiredVtableBytes = 3 * sizeof(ulong);
        if (!TryAdd(moduleBase, definition.VtableRva, out var address)
            || !TryAdd(moduleBase, definition.EnterRva, out var expectedSetup)
            || !TryAdd(moduleBase, definition.LeaveRva, out var expectedCleanup)
            || !IsInsideImage(
                address,
                requiredVtableBytes,
                moduleBase,
                moduleEndExclusive)
            || !memory.TryQueryRegion(address, out var region)
            || !IsValidRange(
                region,
                address,
                requiredVtableBytes,
                moduleBase,
                executable: false)
            || !memory.TryReadUInt64(address, out var firstFunction)
            || firstFunction < moduleBase
            || firstFunction >= moduleEndExclusive
            || !memory.TryReadUInt64(address + sizeof(ulong), out var firstSetup)
            || !memory.TryReadUInt64(
                address + (2 * sizeof(ulong)),
                out var firstCleanup)
            || !memory.TryReadUInt64(address + sizeof(ulong), out var secondSetup)
            || !memory.TryReadUInt64(
                address + (2 * sizeof(ulong)),
                out var secondCleanup)
            || firstSetup != secondSetup
            || firstCleanup != secondCleanup
            || firstSetup != expectedSetup
            || firstCleanup != expectedCleanup)
        {
            throw new InvalidDataException(
                $"{definition.Scene} vtable identity does not match the supported executable.");
        }
    }

    private static bool IsValidRange(
        NativeMemoryRegion region,
        ulong address,
        ulong length,
        ulong moduleBase,
        bool executable)
    {
        if (!region.IsCommitted
            || !region.IsReadable
            || !region.IsImage
            || (executable && !region.IsExecutable)
            || region.AllocationBase != moduleBase
            || region.Size == 0
            || address < region.BaseAddress
            || region.BaseAddress > ulong.MaxValue - (region.Size - 1)
            || address > ulong.MaxValue - (length - 1))
        {
            return false;
        }

        return address + length - 1 <= region.BaseAddress + region.Size - 1;
    }

    private static bool IsInsideImage(
        ulong address,
        ulong length,
        ulong moduleBase,
        ulong moduleEndExclusive) =>
        length > 0
        && address >= moduleBase
        && address <= ulong.MaxValue - (length - 1)
        && address + length - 1 < moduleEndExclusive;

    private static bool TryAdd(ulong left, ulong right, out ulong result)
    {
        result = left + right;
        return result >= left;
    }

    private static void DisableHook<TDelegate>(IHook<TDelegate>? hook)
        where TDelegate : Delegate
    {
        try
        {
            if (hook is { IsHookActivated: true, IsHookEnabled: true })
            {
                hook.Disable();
            }
        }
        catch
        {
            // Reloaded teardown is best-effort.
        }
    }
}
