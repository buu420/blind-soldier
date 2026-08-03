using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Failure-aware reader for the legacy process's own 32-bit FFVII address
/// space. It intentionally cannot serve the Steam 2026 translated guest; that
/// runtime must use <c>TranslatedX86AddressSpace</c>.
/// </summary>
internal sealed class CurrentProcessLegacyAddressSpace : ILegacyAddressSpace
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

    public bool TryWriteInt32(uint virtualAddress, int value)
    {
        if (Environment.Is64BitProcess ||
            virtualAddress == 0 ||
            (ulong)virtualAddress + sizeof(int) > (ulong)uint.MaxValue + 1)
        {
            return false;
        }

        var buffer = BitConverter.GetBytes(value);
        return WriteProcessMemory(
                GetCurrentProcess(),
                new IntPtr(unchecked((int)virtualAddress)),
                buffer,
                (UIntPtr)(uint)buffer.Length,
                out var bytesWritten) &&
            bytesWritten.ToUInt64() == (ulong)buffer.Length;
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
