using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;
using Ff7.Accessibility.Reloaded;

internal static class FieldNavigationPrecisionTests
{
    internal static void Run()
    {
        foreach (var useCheckedReader in new[] { false, true })
        {
            foreach (var raw in new[] { new FieldNavigationFixedPosition(395328, 649280, -4097), new(-105200, -893288, 0) })
            {
                var memory = new Memory(raw);
                var result = memory.Reader(useCheckedReader).ReadNavigation();
                Check(result.IsUsable && result.Position.NativeFixedPosition == raw,
                    "A coherent native navigation read must retain every fixed-point bit.");
                var (module, field, model, x, y, z, triangle, direction) = result.Position;
                Check(module == 1 && field == 453 && model == 0 && x == raw.X >> 12 &&
                      y == raw.Y >> 12 && z == raw.Z >> 12 && triangle == 7 && direction == 192,
                    "Existing integer coordinates and eight-value deconstruction must remain unchanged.");
            }

            var rendered = new Memory(new(395328, 649280, -4097)).Reader(useCheckedReader).Read();
            Check(rendered.IsUsable && rendered.Position.X == 42 && rendered.Position.Y == 77 &&
                  rendered.Position.Z == -10 && rendered.Position.NativeFixedPosition is null,
                "Rendered motion must retain its original coordinates without native metadata.");

            var tornMemory = new Memory(new(395328, 649280, -4097)) { TearFraction = true };
            var torn = tornMemory.Reader(useCheckedReader).ReadNavigation();
            Check(!torn.IsUsable && torn.Position.NativeFixedPosition is null &&
                  torn.Diagnostic.Contains("changed during read", StringComparison.Ordinal),
                "A one-bit fractional change between confirmations must publish no native metadata.");

            var outside = new Memory(new(395328, 649280, -4097));
            outside.SetByte(FieldPositionReader.AddressCurrentModule, 2);
            var invalid = outside.Reader(useCheckedReader).ReadNavigation();
            Check(!invalid.IsUsable && invalid.Position.NativeFixedPosition is null,
                "An invalid native frame must not carry apparently verified precision.");
        }

        var failedMemory = new Memory(new(395328, 649280, -4097))
        {
            FailAddress = FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.ObjectYOffset
        };
        var failed = failedMemory.Reader(true).ReadNavigation();
        Check(!failed.IsUsable && failed.Position.NativeFixedPosition is null,
            "A failed checked raw-coordinate read must publish no precision metadata.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Memory : ILegacyAddressSpace
    {
        private const int ModelBase = 0x100000;
        private readonly Dictionary<uint, byte> bytes = new();
        private readonly int fixedX;
        private int nativeXReads;
        internal bool TearFraction { get; init; }
        internal int? FailAddress { get; init; }

        internal Memory(FieldNavigationFixedPosition raw)
        {
            fixedX = raw.X;
            SetByte(FieldPositionReader.AddressCurrentModule, 1);
            SetWord(FieldPositionReader.AddressFieldId, 453);
            SetWord(FieldPositionReader.AddressFieldCurrentModelId, 0);
            SetByte(FieldPositionReader.AddressFieldNumModels, 5);
            SetInt(FieldPositionReader.AddressFieldModelsPtr, ModelBase);
            SetInt(ModelBase + FieldPositionReader.ModelXOffset, 42);
            SetInt(ModelBase + FieldPositionReader.ModelYOffset, 77);
            SetInt(ModelBase + FieldPositionReader.ModelZOffset, -10);
            SetByte(ModelBase + FieldPositionReader.ModelDirectionOffset, 192);
            SetInt(FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.ObjectXOffset, raw.X);
            SetInt(FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.ObjectYOffset, raw.Y);
            SetInt(FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.ObjectZOffset, raw.Z);
            SetWord(FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.ObjectTriangleOffset, 7);
        }

        internal FieldPositionReader Reader(bool checkedReader) => checkedReader
            ? new FieldPositionReader(this)
            : new FieldPositionReader(ReadInt, address => unchecked((short)ReadWord(address)), ReadWord, ReadByte);

        public bool TryRead(uint address, Span<byte> destination)
        {
            if (FailAddress is { } failed && address == failed) return false;
            if (TearFraction && address == FieldPositionReader.AddressFieldModelsObjs + FieldPositionReader.ObjectXOffset)
                SetInt((int)address, fixedX + nativeXReads++);
            for (var index = 0; index < destination.Length; index++)
                if (!bytes.TryGetValue(address + (uint)index, out destination[index])) return false;
            return true;
        }

        internal void SetByte(int address, byte value) => bytes[(uint)address] = value;
        private void SetWord(int address, ushort value)
        {
            SetByte(address, (byte)value);
            SetByte(address + 1, (byte)(value >> 8));
        }
        private void SetInt(int address, int value)
        {
            for (var index = 0; index < 4; index++) SetByte(address + index, (byte)(value >> (8 * index)));
        }
        private int ReadInt(int address)
        {
            Span<byte> value = stackalloc byte[4];
            if (!TryRead((uint)address, value)) throw new InvalidOperationException("Missing fixture int.");
            return BinaryPrimitives.ReadInt32LittleEndian(value);
        }
        private ushort ReadWord(int address)
        {
            Span<byte> value = stackalloc byte[2];
            if (!TryRead((uint)address, value)) throw new InvalidOperationException("Missing fixture word.");
            return BinaryPrimitives.ReadUInt16LittleEndian(value);
        }
        private byte ReadByte(int address)
        {
            Span<byte> value = stackalloc byte[1];
            if (!TryRead((uint)address, value)) throw new InvalidOperationException("Missing fixture byte.");
            return value[0];
        }
    }
}
