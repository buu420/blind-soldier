using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Ff7.Accessibility.Steam2026X64;

public sealed unsafe class CurrentProcessNativeMemoryReader : INativeMemoryReader, INativeMemoryWriter
{
    private const uint MemoryStateCommit = 0x1000;
    private const uint PageNoAccess = 0x01;
    private const uint PageReadableMask = 0x02 | 0x04 | 0x08 | 0x20 | 0x40 | 0x80;
    private const uint PageExecuteMask = 0x10 | 0x20 | 0x40 | 0x80;

    /// <summary>PAGE_READWRITE and PAGE_EXECUTE_READWRITE. Nothing else takes a write.</summary>
    private const uint PageWritableMask = 0x04 | 0x40;

    /// <summary>
    /// PAGE_WRITECOPY and PAGE_EXECUTE_WRITECOPY. A write to one of these succeeds
    /// and then goes nowhere the game will look: it forks a private copy of the
    /// page for us alone. Refused rather than trusted.
    /// </summary>
    private const uint PageCopyOnWriteMask = 0x08 | 0x80;

    private const uint PageGuard = 0x100;
    private const uint MemoryTypeImage = 0x1000000;

    private readonly nint processHandle;

    public CurrentProcessNativeMemoryReader()
    {
        // GetCurrentProcess returns the stable Win32 pseudo-handle (-1). It is valid for the
        // lifetime of this process and must not be closed, unlike a Handle borrowed from a
        // temporary System.Diagnostics.Process object that a finalizer may later release.
        processHandle = GetCurrentProcess();
    }

    public bool TryReadUInt64(ulong address, out ulong value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        var success = TryRead(address, buffer);
        value = success ? BinaryPrimitives.ReadUInt64LittleEndian(buffer) : 0;
        return success;
    }

    public bool TryRead(ulong address, Span<byte> destination)
    {
        if (address == 0)
        {
            destination.Clear();
            return false;
        }

        if (destination.IsEmpty)
        {
            return true;
        }

        fixed (byte* destinationPointer = destination)
        {
            var success = ReadProcessMemory(
                processHandle,
                (nint)address,
                destinationPointer,
                (nuint)destination.Length,
                out var bytesRead);
            if (!success || bytesRead != (nuint)destination.Length)
            {
                destination.Clear();
                return false;
            }
        }

        return true;
    }

    public bool TryQueryRegion(ulong address, out NativeMemoryRegion region)
    {
        region = default;
        if (address == 0)
        {
            return false;
        }

        var result = VirtualQueryEx(
            processHandle,
            (nint)address,
            out var nativeRegion,
            (nuint)Marshal.SizeOf<MemoryBasicInformation>());
        if (result == 0 || nativeRegion.RegionSize == 0)
        {
            return false;
        }

        var protection = nativeRegion.Protect;
        var readable = (protection & PageReadableMask) != 0
                       && (protection & (PageGuard | PageNoAccess)) == 0;
        var executable = (protection & PageExecuteMask) != 0
                         && (protection & (PageGuard | PageNoAccess)) == 0;
        var writable = (protection & PageWritableMask) != 0
                       && (protection & (PageGuard | PageNoAccess)) == 0;
        var copyOnWrite = (protection & PageCopyOnWriteMask) != 0;
        region = new NativeMemoryRegion(
            (ulong)(nuint)nativeRegion.BaseAddress,
            (ulong)nativeRegion.RegionSize,
            (ulong)(nuint)nativeRegion.AllocationBase,
            nativeRegion.State == MemoryStateCommit,
            executable,
            nativeRegion.Type == MemoryTypeImage,
            readable,
            writable,
            copyOnWrite);
        return true;
    }

    public bool TryExchangeInt32(ulong hostAddress, int value)
    {
        // Four-byte aligned, or the exchange is not indivisible and the game can
        // observe half a cursor.
        if (hostAddress == 0 || (hostAddress & 3) != 0)
        {
            return false;
        }

        if (!TryQueryRegion(hostAddress, out var region) ||
            !region.IsCommitted ||
            !region.IsWritable ||
            region.IsCopyOnWrite)
        {
            return false;
        }

        // All four bytes inside the one region that was just vetted.
        var end = hostAddress + sizeof(int);
        if (end > region.BaseAddress + region.Size)
        {
            return false;
        }

        // The game shares this process, so its memory is directly addressable and
        // an interlocked exchange is available. That is the whole reason for
        // writing this way rather than through WriteProcessMemory.
        try
        {
            Interlocked.Exchange(ref *(int*)hostAddress, value);
            return true;
        }
        catch (AccessViolationException)
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool ReadProcessMemory(
        nint processHandle,
        nint baseAddress,
        void* buffer,
        nuint size,
        out nuint bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nuint VirtualQueryEx(
        nint processHandle,
        nint address,
        out MemoryBasicInformation buffer,
        nuint length);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }
}
