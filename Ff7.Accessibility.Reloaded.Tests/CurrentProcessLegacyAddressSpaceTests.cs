using System.Runtime.InteropServices;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class CurrentProcessLegacyAddressSpaceTests
{
    public static void Run()
    {
        if (Environment.Is64BitProcess)
        {
            throw new InvalidOperationException("The legacy current-process address-space test must run as x86.");
        }

        var pointer = Marshal.AllocHGlobal(8);
        try
        {
            var expected = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
            Marshal.Copy(expected, 0, pointer, expected.Length);
            var addressSpace = new CurrentProcessLegacyAddressSpace();
            Span<byte> actual = stackalloc byte[expected.Length];
            if (!addressSpace.TryRead(unchecked((uint)pointer.ToInt32()), actual) ||
                !actual.SequenceEqual(expected))
            {
                throw new InvalidOperationException("Expected the x86 current-process address space to read the complete mapped range.");
            }

            actual.Fill(0xCC);
            if (addressSpace.TryRead(1, actual) || actual.ToArray().Any(value => value != 0))
            {
                throw new InvalidOperationException("An unreadable guest range must fail and clear its destination.");
            }

            if (addressSpace.TryRead(uint.MaxValue - 1, actual))
            {
                throw new InvalidOperationException("A wrapping guest range must fail closed.");
            }

            const int writtenValue = 600;
            if (!addressSpace.TryWriteInt32(unchecked((uint)pointer.ToInt32()), writtenValue) ||
                Marshal.ReadInt32(pointer) != writtenValue)
            {
                throw new InvalidOperationException("Expected the x86 current-process address space to write one complete Int32.");
            }

            if (addressSpace.TryWriteInt32(0, writtenValue) ||
                addressSpace.TryWriteInt32(uint.MaxValue - 1, writtenValue))
            {
                throw new InvalidOperationException("Invalid native writes must fail closed.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }
}
