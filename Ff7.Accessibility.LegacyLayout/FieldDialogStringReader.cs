using System.Buffers.Binary;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class FieldDialogStringReader
{
    public const int AddressCurrentDialogStringPointer = 0x00CBF578;

    private const int MaxDialogTextLength = 0x200;
    private const int MaxVisibleDialogTextLength = FieldMessageReader.FieldTextBufferLength;
    private const int MinimumReadablePointer = 0x00400000;
    private const int MaximumReadablePointer = 0x7fffffff;

    private readonly Func<int, int>? readInt32;
    private readonly Func<int, int, string>? readText;
    private readonly Func<int>? readCurrentModule;
    private readonly Func<int, int, bool>? isReadableMemory;
    private readonly ILegacyAddressSpace? addressSpace;

    public FieldDialogStringReader(
        Func<int, int> readInt32,
        Func<int, int, string> readText,
        Func<int>? readCurrentModule = null,
        Func<int, int, bool>? isReadableMemory = null)
    {
        this.readInt32 = readInt32;
        this.readText = readText;
        this.readCurrentModule = readCurrentModule;
        this.isReadableMemory = isReadableMemory ?? ((_, _) => true);
    }

    public FieldDialogStringReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public FieldMessageCandidate ReadCurrent()
    {
        if (addressSpace is not null)
        {
            return TryReadCurrent(out var checkedCandidate)
                ? checkedCandidate
                : new FieldMessageCandidate(string.Empty, string.Empty);
        }

        if (readCurrentModule is not null && readCurrentModule() != FieldPositionReader.FieldModule)
        {
            return new FieldMessageCandidate(string.Empty, string.Empty);
        }

        var address = readInt32!(AddressCurrentDialogStringPointer);
        if (address is < MinimumReadablePointer or > MaximumReadablePointer)
        {
            return new FieldMessageCandidate(string.Empty, string.Empty);
        }

        if (!isReadableMemory!(address, MaxDialogTextLength))
        {
            return new FieldMessageCandidate(string.Empty, string.Empty);
        }

        var text = Ff7EncodedTextDecoder.NormalizeWhitespace(readText!(address, MaxDialogTextLength) ?? string.Empty);
        return text.Length == 0
            ? new FieldMessageCandidate(string.Empty, string.Empty)
            : new FieldMessageCandidate("dialog pointer", text);
    }

    public bool TryReadCurrent(out FieldMessageCandidate candidate)
    {
        candidate = new FieldMessageCandidate(string.Empty, string.Empty);
        if (addressSpace is null ||
            !TryCaptureFrame(out var before) ||
            before.Module != FieldPositionReader.FieldModule ||
            before.MessageDataPointer == 0 ||
            !TryAdd(before.MessageDataPointer, FieldMessageReader.FieldMessageDataRange - 1, out var messageDataEnd) ||
            !TryReadVisibleBuffers(before, messageDataEnd, out var firstReads) ||
            !TryReadVisibleBuffers(before, messageDataEnd, out var secondReads) ||
            !VisibleReadsEqual(firstReads, secondReads) ||
            !TryCaptureFrame(out var after) ||
            !before.Equals(after))
        {
            return false;
        }

        for (var index = 0; index < firstReads.Length; index++)
        {
            if (!firstReads[index].IsReadable)
            {
                continue;
            }

            candidate = new FieldMessageCandidate($"dialog window {index}", firstReads[index].Text);
            return true;
        }

        return false;
    }

    private bool TryReadVisibleBuffers(
        CheckedFrame frame,
        uint messageDataEnd,
        out VisibleDialogRead[] reads)
    {
        reads = new VisibleDialogRead[FieldMessageReader.WindowCount];
        for (var index = 0; index < reads.Length; index++)
        {
            if (frame.States[index] == FieldMessageReader.FreeWindowState)
            {
                continue;
            }

            var pointer = frame.Pointers[index];
            if (pointer < frame.MessageDataPointer || pointer > messageDataEnd ||
                !TryAdd(
                    (uint)FieldMessageReader.AddressFieldWindowTextBuffers,
                    (ulong)index * FieldMessageReader.WindowTextBufferStride,
                    out var bufferAddress))
            {
                return false;
            }

            if (!LegacyFf7TextReader.TryReadTerminated(
                    addressSpace!,
                    bufferAddress,
                    MaxVisibleDialogTextLength,
                    out var bytes,
                    out var text))
            {
                reads = [];
                return false;
            }

            var isReadable = text.Length != 0;
            reads[index] = new VisibleDialogRead(isReadable, bytes, text);
        }

        return true;
    }

    private bool TryCaptureFrame(out CheckedFrame frame)
    {
        frame = default;
        var memory = addressSpace!;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            !memory.TryReadUInt32((uint)FieldMessageReader.AddressFieldMessageDataPointer, out var messageDataPointer))
        {
            return false;
        }

        var states = new byte[FieldMessageReader.WindowCount];
        var pointerBytes = new byte[FieldMessageReader.WindowCount * sizeof(uint)];
        if (!memory.TryRead((uint)FieldMessageReader.AddressFieldWindowStates, states) ||
            !memory.TryRead((uint)AddressCurrentDialogStringPointer, pointerBytes))
        {
            return false;
        }

        var pointers = new uint[FieldMessageReader.WindowCount];
        for (var index = 0; index < pointers.Length; index++)
        {
            pointers[index] = BinaryPrimitives.ReadUInt32LittleEndian(
                pointerBytes.AsSpan(index * sizeof(uint), sizeof(uint)));
        }

        frame = new CheckedFrame(module, fieldId, messageDataPointer, states, pointers);
        return true;
    }

    private static bool VisibleReadsEqual(VisibleDialogRead[] left, VisibleDialogRead[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            if (left[index].IsReadable != right[index].IsReadable ||
                left[index].Text != right[index].Text ||
                !left[index].Bytes.AsSpan().SequenceEqual(right[index].Bytes))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryAdd(uint address, ulong offset, out uint result)
    {
        var sum = (ulong)address + offset;
        result = sum <= uint.MaxValue ? (uint)sum : 0;
        return sum <= uint.MaxValue;
    }

    private readonly record struct VisibleDialogRead(bool IsReadable, byte[] Bytes, string Text);

    private readonly struct CheckedFrame : IEquatable<CheckedFrame>
    {
        public CheckedFrame(byte module, ushort fieldId, uint messageDataPointer, byte[] states, uint[] pointers)
        {
            Module = module;
            FieldId = fieldId;
            MessageDataPointer = messageDataPointer;
            States = states;
            Pointers = pointers;
        }

        public byte Module { get; }
        public ushort FieldId { get; }
        public uint MessageDataPointer { get; }
        public byte[] States { get; }
        public uint[] Pointers { get; }

        public bool Equals(CheckedFrame other) =>
            Module == other.Module &&
            FieldId == other.FieldId &&
            MessageDataPointer == other.MessageDataPointer &&
            States.AsSpan().SequenceEqual(other.States) &&
            Pointers.AsSpan().SequenceEqual(other.Pointers);
    }
}
