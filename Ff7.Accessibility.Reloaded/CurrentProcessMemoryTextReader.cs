using System.Runtime.InteropServices;

namespace Ff7.Accessibility.Reloaded;

internal static class CurrentProcessMemoryTextReader
{
    private const int MaximumTextReadLength = 0x10000;

    public static string ReadFf7EncodedText(int address, int maxLength)
    {
        if (!TryReadBytes(address, maxLength, out var bytes))
        {
            return string.Empty;
        }

        return Ff7EncodedTextDecoder.DecodeTerminated(bytes);
    }

    private static bool TryReadBytes(int address, int length, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (address <= 0 || length <= 0 || length > MaximumTextReadLength)
        {
            return false;
        }

        var buffer = new byte[length];
        if (!ReadProcessMemory(
                GetCurrentProcess(),
                new IntPtr(address),
                buffer,
                (UIntPtr)(uint)length,
                out var bytesRead) ||
            bytesRead.ToUInt64() != (ulong)length)
        {
            return false;
        }

        bytes = buffer;
        return true;
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
}
