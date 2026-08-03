using System.Text;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class FieldLineBufferSnapshotTests
{
    public static void Run()
    {
        var memory = CreateMemory();
        var reader = new FieldMessageReader(memory);
        if (!reader.TryReadLineBuffer(out var candidate) ||
            candidate.Source != "line" ||
            candidate.Text != "Sector 7")
        {
            throw new InvalidOperationException("Expected a coherent native field line-buffer snapshot.");
        }

        var torn = CreateMemory();
        torn.TearAddress = FieldMessageReader.AddressFieldMessageLineBuffer;
        var tornReader = new FieldMessageReader(torn);
        if (tornReader.TryReadLineBuffer(out _))
        {
            throw new InvalidOperationException("A changing native line buffer must fail closed.");
        }

        var unmapped = CreateMemory();
        unmapped.Unmap(FieldMessageReader.AddressFieldMessageLineBuffer);
        if (new FieldMessageReader(unmapped).TryReadLineBuffer(out _))
        {
            throw new InvalidOperationException("An unmapped native line buffer must fail closed.");
        }
    }

    private static Memory CreateMemory()
    {
        var memory = new Memory();
        memory.WriteByte(FieldPositionReader.AddressCurrentModule, FieldPositionReader.FieldModule);
        memory.WriteUInt16(FieldPositionReader.AddressFieldId, 116);
        memory.WriteUInt32(FieldMessageReader.AddressFieldMessageDataPointer, 0x02000000);
        for (var index = 0; index < FieldMessageReader.WindowCount; index++)
        {
            memory.WriteByte(FieldMessageReader.AddressFieldWindowStates + index, FieldMessageReader.FreeWindowState);
            memory.WriteUInt32(FieldMessageReader.AddressFieldWindowMessagePointers + index * sizeof(uint), 0);
        }

        var line = new byte[FieldMessageReader.FieldTextBufferLength];
        Array.Fill(line, (byte)0xFF);
        var text = Encoding.ASCII.GetBytes("Sector 7");
        for (var index = 0; index < text.Length; index++)
        {
            line[index] = checked((byte)(text[index] - 0x20));
        }
        memory.Write(FieldMessageReader.AddressFieldMessageLineBuffer, line);
        return memory;
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private readonly Dictionary<uint, byte> bytes = [];
        private int tearReads;

        public int? TearAddress { get; set; }

        public bool TryRead(uint virtualAddress, Span<byte> destination)
        {
            for (var index = 0; index < destination.Length; index++)
            {
                if (!bytes.TryGetValue(virtualAddress + (uint)index, out destination[index]))
                {
                    destination.Clear();
                    return false;
                }
            }

            if (TearAddress == unchecked((int)virtualAddress) && ++tearReads == 2)
            {
                destination[0] ^= 1;
            }

            return virtualAddress != 0;
        }

        public void WriteByte(int address, byte value) => bytes[(uint)address] = value;

        public void WriteUInt16(int address, ushort value) => Write(address, BitConverter.GetBytes(value));

        public void WriteUInt32(int address, uint value) => Write(address, BitConverter.GetBytes(value));

        public void Write(int address, IReadOnlyList<byte> values)
        {
            for (var index = 0; index < values.Count; index++)
            {
                bytes[(uint)address + (uint)index] = values[index];
            }
        }

        public void Unmap(int address)
        {
            for (var index = 0; index < FieldMessageReader.FieldTextBufferLength; index++)
            {
                bytes.Remove((uint)address + (uint)index);
            }
        }
    }
}
