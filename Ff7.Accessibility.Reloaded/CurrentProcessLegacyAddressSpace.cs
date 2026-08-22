using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Failure-aware reader for the legacy process's own 32-bit FFVII address
/// space. It intentionally cannot serve the Steam 2026 translated guest; that
/// runtime must use <c>TranslatedX86AddressSpace</c>.
/// </summary>
internal sealed class CurrentProcessLegacyAddressSpace : ILegacyAddressSpace, ILegacyMemoryWriter
{
    public bool TryRead(uint virtualAddress, Span<byte> destination)
    {
        if (destination.Length == 0)
        {
            return false;
        }

        destination.Clear();
        if (Environment.Is64BitProcess || virtualAddress == 0)
        {
            return false;
        }

        var endExclusive = (ulong)virtualAddress + (uint)destination.Length;
        if (endExclusive > (ulong)uint.MaxValue + 1)
        {
            return false;
        }

        var buffer = new byte[destination.Length];
        if (!ReadProcessMemory(
                GetCurrentProcess(),
                new IntPtr(unchecked((int)virtualAddress)),
                buffer,
                (UIntPtr)(uint)buffer.Length,
                out var bytesRead) ||
            bytesRead.ToUInt64() != (ulong)buffer.Length)
        {
            return false;
        }

        buffer.AsSpan().CopyTo(destination);
        return true;
    }

    /// <summary>
    /// Atomically replaces the 32-bit value at a guest address in this process.
    /// </summary>
    /// <remarks>
    /// The mod is loaded into the game, so a guest address here is directly
    /// addressable and an interlocked exchange is available. That matters: the
    /// value being replaced is read by running game code, and WriteProcessMemory
    /// promises only to validate and copy the range - it does not promise the game
    /// cannot observe half of it. An aligned interlocked exchange is documented to
    /// be indivisible.
    ///
    /// <para>The region is vetted first rather than relying on catching the fault.
    /// An access violation is a corrupted-state exception and will take the game
    /// down rather than return false, so the check has to happen before the write,
    /// not around it. This mirrors the Steam 2026 runtime's writer, which resolves
    /// through the translator's page table and then performs the same vetting - one
    /// rule on both runtimes.</para>
    /// </remarks>
    public unsafe bool TryWriteInt32(uint virtualAddress, int value)
    {
        if (Environment.Is64BitProcess ||
            virtualAddress == 0 ||
            (virtualAddress & 3) != 0 ||
            (ulong)virtualAddress + sizeof(int) > (ulong)uint.MaxValue + 1)
        {
            return false;
        }

        if (!IsCommittedWritable(virtualAddress, sizeof(int)))
        {
            return false;
        }

        Interlocked.Exchange(ref *(int*)virtualAddress, value);

        // Report what actually happened rather than that the call returned.
        return Volatile.Read(ref *(int*)virtualAddress) == value;
    }

    /// <summary>
    /// Whether the whole span sits in one committed, writable, non-guard,
    /// non-copy-on-write region.
    /// </summary>
    /// <remarks>
    /// Copy-on-write is refused rather than trusted: writing to such a page appears
    /// to succeed and then forks a private copy that the game will never read, so
    /// the cursor would report as moved and not be.
    /// </remarks>
    private static bool IsCommittedWritable(uint virtualAddress, int length)
    {
        var information = default(MemoryBasicInformation);
        var queried = VirtualQuery(
            new IntPtr(unchecked((int)virtualAddress)),
            ref information,
            (UIntPtr)(uint)Marshal.SizeOf<MemoryBasicInformation>());
        if (queried == UIntPtr.Zero || information.State != MemoryStateCommit)
        {
            return false;
        }

        const uint writable = PageReadWrite | PageExecuteReadWrite;
        const uint copyOnWrite = PageWriteCopy | PageExecuteWriteCopy;
        if ((information.Protect & writable) == 0 ||
            (information.Protect & (PageGuard | PageNoAccess)) != 0 ||
            (information.Protect & copyOnWrite) != 0)
        {
            return false;
        }

        var regionBase = (ulong)(nuint)information.BaseAddress;
        return virtualAddress >= regionBase &&
            (ulong)virtualAddress + (ulong)length <= regionBase + (ulong)information.RegionSize;
    }

    private const uint MemoryStateCommit = 0x1000;
    private const uint PageNoAccess = 0x01;
    private const uint PageReadWrite = 0x04;
    private const uint PageWriteCopy = 0x08;
    private const uint PageExecuteReadWrite = 0x40;
    private const uint PageExecuteWriteCopy = 0x80;
    private const uint PageGuard = 0x100;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr VirtualQuery(
        IntPtr address,
        ref MemoryBasicInformation buffer,
        UIntPtr length);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        [Out] byte[] buffer,
        UIntPtr size,
        out UIntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        [In] byte[] buffer,
        UIntPtr size,
        out UIntPtr bytesWritten);
}
