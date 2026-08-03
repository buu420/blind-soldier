using System.Collections.ObjectModel;
using System.Text;
using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

public sealed class FieldMessageReader
{
    public const int AddressFieldMessageDataPointer = 0x00CC08E8;
    public const int AddressFieldMessageLineBuffer = 0x00DC0C44;
    public const int AddressFieldWindowTextBuffers = 0x00CC0428;
    public const int AddressFieldWindowMessagePointers = 0x00CBF578;
    public const int AddressFieldWindowStates = 0x00CC0960;
    public const int WindowCount = 4;
    public const int WindowTextBufferStride = 0x100;
    public const int FieldTextBufferLength = 0x100;
    public const int FieldMessageDataRange = 0x10000;
    public const byte FreeWindowState = 0xff;

    private readonly Func<int, int>? readInt32;
    private readonly Func<int, byte>? readByte;
    private readonly Func<int, int, string>? readText;
    private readonly ILegacyAddressSpace? addressSpace;

    public FieldMessageReader(Func<int, int> readInt32, Func<int, byte> readByte, Func<int, int, string> readText)
    {
        this.readInt32 = readInt32;
        this.readByte = readByte;
        this.readText = readText;
    }

    public FieldMessageReader(ILegacyAddressSpace addressSpace)
    {
        this.addressSpace = addressSpace ?? throw new ArgumentNullException(nameof(addressSpace));
    }

    public FieldMessageCandidate ReadCurrent()
    {
        if (addressSpace is not null)
        {
            throw new InvalidOperationException("Use TryReadVisibleWindows for checked visible-window snapshots.");
        }

        var best = new FieldMessageCandidate(string.Empty, string.Empty);
        var bestPriority = -1;

        ConsiderActiveWindowBuffers(ref best, ref bestPriority);
        Consider("line", AddressFieldMessageLineBuffer, priority: 0, ref best, ref bestPriority);

        return best;
    }

    public bool TryReadVisibleWindows(out IReadOnlyList<FieldVisibleWindowSnapshot> windows)
    {
        windows = Array.Empty<FieldVisibleWindowSnapshot>();
        if (addressSpace is null || !TryCaptureFrame(out var before) || before.Module != FieldPositionReader.FieldModule)
        {
            return false;
        }

        var visible = new List<FieldVisibleWindowSnapshot>(WindowCount);
        var firstReads = new (bool IsValid, byte[] Bytes, string Text)[WindowCount];
        for (var index = 0; index < WindowCount; index++)
        {
            if (before.States[index] == FreeWindowState)
            {
                continue;
            }

            var bufferAddress = (uint)(AddressFieldWindowTextBuffers + index * WindowTextBufferStride);
            if (!LegacyFf7TextReader.TryReadTerminated(
                    addressSpace,
                    bufferAddress,
                    FieldTextBufferLength,
                    out var bytes,
                    out var text))
            {
                return false;
            }

            var isValid = text.Length != 0;
            firstReads[index] = (isValid, bytes, text);
            if (isValid)
            {
                visible.Add(new FieldVisibleWindowSnapshot(index, before.States[index], text, before.Pointers[index]));
            }
        }

        if (!TryCaptureFrame(out var middle) || !before.Equals(middle))
        {
            return false;
        }

        for (var index = 0; index < WindowCount; index++)
        {
            if (before.States[index] == FreeWindowState)
            {
                continue;
            }

            var bufferAddress = (uint)(AddressFieldWindowTextBuffers + index * WindowTextBufferStride);
            if (!LegacyFf7TextReader.TryReadTerminated(
                    addressSpace,
                    bufferAddress,
                    FieldTextBufferLength,
                    out var bytes,
                    out var text))
            {
                return false;
            }

            var isValid = text.Length != 0;
            var first = firstReads[index];
            if (isValid != first.IsValid ||
                isValid && (!bytes.AsSpan().SequenceEqual(first.Bytes) || text != first.Text))
            {
                return false;
            }
        }

        if (!TryCaptureFrame(out var after) || !before.Equals(after))
        {
            return false;
        }

        windows = new ReadOnlyCollection<FieldVisibleWindowSnapshot>(visible);
        return true;
    }

    public bool HasReadableActiveWindow()
    {
        if (addressSpace is not null)
        {
            return TryHasReadableActiveWindow(out var readable) && readable;
        }

        var messageDataPointer = readInt32!(AddressFieldMessageDataPointer);
        for (var i = 0; i < WindowCount; i++)
        {
            if (!IsWindowSlotActive(i))
            {
                continue;
            }

            var textPointer = readInt32(AddressFieldWindowMessagePointers + (i * sizeof(int)));
            if (IsInsideFieldMessageData(messageDataPointer, textPointer) && NormalizeText(textPointer).Length != 0)
            {
                return true;
            }

            if (NormalizeText(AddressFieldWindowTextBuffers + (i * WindowTextBufferStride)).Length != 0)
            {
                return true;
            }
        }

        return false;
    }

    public bool TryHasReadableActiveWindow(out bool readable)
    {
        readable = false;
        if (!TryReadVisibleWindows(out var windows))
        {
            return false;
        }

        readable = windows.Count != 0;
        return true;
    }

    public bool TryReadLineBuffer(out FieldMessageCandidate candidate)
    {
        candidate = new FieldMessageCandidate(string.Empty, string.Empty);
        if (addressSpace is null ||
            !TryCaptureFrame(out var before) ||
            before.Module != FieldPositionReader.FieldModule ||
            !LegacyFf7TextReader.TryReadTerminated(
                addressSpace,
                AddressFieldMessageLineBuffer,
                FieldTextBufferLength,
                out var firstBytes,
                out var firstText) ||
            !TryCaptureFrame(out var middle) ||
            !before.Equals(middle) ||
            !LegacyFf7TextReader.TryReadTerminated(
                addressSpace,
                AddressFieldMessageLineBuffer,
                FieldTextBufferLength,
                out var secondBytes,
                out var secondText) ||
            !firstBytes.AsSpan().SequenceEqual(secondBytes) ||
            !string.Equals(firstText, secondText, StringComparison.Ordinal) ||
            !TryCaptureFrame(out var after) ||
            !before.Equals(after))
        {
            return false;
        }

        var text = Ff7EncodedTextDecoder.NormalizeWhitespace(firstText);
        if (text.Length == 0)
        {
            return false;
        }

        candidate = new FieldMessageCandidate("line", text);
        return true;
    }

    public FieldMessageDiagnostics ReadDiagnostics()
    {
        if (addressSpace is not null)
        {
            throw new InvalidOperationException("Checked diagnostics require a dedicated snapshot contract.");
        }

        var messageDataPointer = readInt32!(AddressFieldMessageDataPointer);
        var windows = new List<FieldMessageWindowDiagnostic>(WindowCount);
        for (var i = 0; i < WindowCount; i++)
        {
            var state = readByte!(AddressFieldWindowStates + i);
            var stateInt32 = readInt32(AddressFieldWindowStates + (i * sizeof(int)));
            var pointer = readInt32(AddressFieldWindowMessagePointers + (i * sizeof(int)));
            var pointerInsideMessageData = IsInsideFieldMessageData(messageDataPointer, pointer);
            var pointerText = state != FreeWindowState && pointerInsideMessageData
                ? NormalizeText(pointer)
                : string.Empty;
            var bufferAddress = AddressFieldWindowTextBuffers + (i * WindowTextBufferStride);
            var bufferText = NormalizeText(bufferAddress);
            windows.Add(new FieldMessageWindowDiagnostic(
                i,
                state,
                stateInt32,
                unchecked((uint)pointer),
                pointerInsideMessageData,
                pointerText,
                unchecked((uint)bufferAddress),
                bufferText));
        }

        return new FieldMessageDiagnostics(
            unchecked((uint)messageDataPointer),
            windows,
            NormalizeText(AddressFieldMessageLineBuffer));
    }

    public FieldMessageCandidate ReadMessageById(int messageId)
    {
        if (addressSpace is not null)
        {
            return TryReadMessageById(messageId, out var checkedCandidate)
                ? checkedCandidate
                : new FieldMessageCandidate(string.Empty, string.Empty);
        }

        if (!TryResolveMessageTextPointer(messageId, out _, out var textPointer))
        {
            return new FieldMessageCandidate(string.Empty, string.Empty);
        }

        var text = NormalizeText(textPointer);
        return text.Length == 0
            ? new FieldMessageCandidate(string.Empty, string.Empty)
            : new FieldMessageCandidate($"message {messageId}", text);
    }

    public bool TryReadMessageById(int messageId, out FieldMessageCandidate candidate)
    {
        candidate = new FieldMessageCandidate(string.Empty, string.Empty);
        if (!TryReadCheckedMessageBytes(messageId, out var bytes, out var before))
        {
            return false;
        }

        var text = Ff7EncodedTextDecoder.NormalizeWhitespace(Ff7EncodedTextDecoder.Decode(bytes));
        if (text.Length == 0)
        {
            return false;
        }

        candidate = new FieldMessageCandidate($"message {messageId}", text);
        return true;
    }

    public IReadOnlyList<string> ReadMessageLinesById(int messageId)
    {
        if (addressSpace is not null)
        {
            return TryReadMessageLinesById(messageId, out var lines)
                ? lines
                : Array.Empty<string>();
        }

        if (!TryResolveMessageTextPointer(messageId, out var messageDataPointer, out var textPointer))
        {
            return Array.Empty<string>();
        }

        var bytes = new List<byte>(FieldTextBufferLength);
        var maximumLength = messageDataPointer + FieldMessageDataRange - textPointer;
        for (var offset = 0; offset < maximumLength; offset++)
        {
            var value = readByte!(textPointer + offset);
            bytes.Add(value);
            if (value == 0xff)
            {
                return Ff7EncodedTextDecoder.DecodeLines(bytes.ToArray());
            }
        }

        return Array.Empty<string>();
    }

    public IReadOnlyList<Ff7DecodedTextPage> ReadMessagePagesById(int messageId)
    {
        if (addressSpace is not null)
        {
            return TryReadMessagePagesById(messageId, out var pages)
                ? pages
                : Array.Empty<Ff7DecodedTextPage>();
        }

        if (!TryResolveMessageTextPointer(messageId, out var messageDataPointer, out var textPointer))
        {
            return Array.Empty<Ff7DecodedTextPage>();
        }

        var bytes = new List<byte>(FieldTextBufferLength);
        var maximumLength = messageDataPointer + FieldMessageDataRange - textPointer;
        for (var offset = 0; offset < maximumLength; offset++)
        {
            var value = readByte!(textPointer + offset);
            bytes.Add(value);
            if (value == 0xff)
            {
                return Ff7EncodedTextDecoder.DecodePages(bytes.ToArray());
            }
        }

        return Array.Empty<Ff7DecodedTextPage>();
    }

    public bool TryReadMessageLinesById(int messageId, out IReadOnlyList<string> lines)
    {
        lines = Array.Empty<string>();
        if (!TryReadCheckedMessageBytes(messageId, out var bytes, out _))
        {
            return false;
        }

        lines = Ff7EncodedTextDecoder.DecodeLines(bytes);
        return true;
    }

    public bool TryReadMessagePagesById(
        int messageId,
        out IReadOnlyList<Ff7DecodedTextPage> pages)
    {
        pages = Array.Empty<Ff7DecodedTextPage>();
        if (!TryReadCheckedMessageBytes(messageId, out var bytes, out _))
        {
            return false;
        }

        pages = Ff7EncodedTextDecoder.DecodePages(bytes);
        return pages.Count != 0;
    }

    private bool TryReadCheckedMessageBytes(int messageId, out byte[] bytes, out CheckedFrame before)
    {
        bytes = [];
        before = default;
        if (addressSpace is null || messageId < 0 ||
            !TryCaptureFrame(out before) || before.Module != FieldPositionReader.FieldModule ||
            before.MessageDataPointer == 0 ||
            !TryReadMessageTableEntry(
                before.MessageDataPointer,
                messageId,
                out var messageCount,
                out var textOffset,
                out var textAddress))
        {
            return false;
        }

        if (!LegacyFf7TextReader.TryReadTerminated(
                addressSpace,
                textAddress,
                FieldMessageDataRange - textOffset,
                out bytes,
                out _) ||
            !TryCaptureFrame(out var middle) || !before.Equals(middle) ||
            !LegacyFf7TextReader.TryReadTerminated(
                addressSpace,
                textAddress,
                FieldMessageDataRange - textOffset,
                out var bytesAfter,
                out _) ||
            !bytes.AsSpan().SequenceEqual(bytesAfter) ||
            !TryReadMessageTableEntry(
                before.MessageDataPointer,
                messageId,
                out var messageCountAfter,
                out var textOffsetAfter,
                out var textAddressAfter) ||
            messageCount != messageCountAfter ||
            textOffset != textOffsetAfter ||
            textAddress != textAddressAfter ||
            !TryCaptureFrame(out var after) || !before.Equals(after))
        {
            bytes = [];
            return false;
        }

        return true;
    }

    private bool TryReadMessageTableEntry(
        uint messageDataPointer,
        int messageId,
        out ushort messageCount,
        out ushort textOffset,
        out uint textAddress)
    {
        messageCount = 0;
        textOffset = 0;
        textAddress = 0;
        var memory = addressSpace;
        if (memory is null || messageDataPointer == 0 || messageId < 0 ||
            !TryAdd(messageDataPointer, FieldMessageDataRange - 1, out _) ||
            !memory.TryReadUInt16(messageDataPointer, out messageCount) ||
            messageId >= messageCount)
        {
            return false;
        }

        var tableLength = 2UL + (ulong)messageCount * sizeof(ushort);
        var tableOffset = 2UL + (ulong)messageId * sizeof(ushort);
        if (tableLength > FieldMessageDataRange ||
            tableOffset + sizeof(ushort) > tableLength ||
            !TryAdd(messageDataPointer, tableLength - 1, out _) ||
            !TryAdd(messageDataPointer, tableOffset, out var tableAddress) ||
            !memory.TryReadUInt16(tableAddress, out textOffset) ||
            (ulong)textOffset < tableLength ||
            !TryAdd(messageDataPointer, textOffset, out textAddress))
        {
            messageCount = 0;
            textOffset = 0;
            textAddress = 0;
            return false;
        }

        return true;
    }

    private bool TryCaptureFrame(out CheckedFrame frame)
    {
        frame = default;
        var memory = addressSpace!;
        if (!memory.TryReadByte((uint)FieldPositionReader.AddressCurrentModule, out var module) ||
            !memory.TryReadUInt16((uint)FieldPositionReader.AddressFieldId, out var fieldId) ||
            !memory.TryReadUInt32(AddressFieldMessageDataPointer, out var messageDataPointer))
        {
            return false;
        }

        var states = new byte[WindowCount];
        var pointers = new uint[WindowCount];
        for (var index = 0; index < WindowCount; index++)
        {
            if (!memory.TryReadByte((uint)(AddressFieldWindowStates + index), out states[index]) ||
                !memory.TryReadUInt32((uint)(AddressFieldWindowMessagePointers + index * sizeof(uint)), out pointers[index]))
            {
                return false;
            }
        }

        frame = new CheckedFrame(module, fieldId, messageDataPointer, states, pointers);
        return true;
    }

    private bool TryResolveMessageTextPointer(int messageId, out int messageDataPointer, out int textPointer)
    {
        messageDataPointer = 0;
        textPointer = 0;
        if (messageId < 0)
        {
            return false;
        }

        messageDataPointer = readInt32!(AddressFieldMessageDataPointer);
        if (messageDataPointer <= 0)
        {
            return false;
        }

        var offsetTableEntry = messageDataPointer + 2 + (messageId * 2);
        textPointer = messageDataPointer +
            readByte!(offsetTableEntry) +
            (readByte(offsetTableEntry + 1) * 0x100);
        return IsInsideFieldMessageData(messageDataPointer, textPointer);
    }

    private void ConsiderActiveWindowBuffers(ref FieldMessageCandidate best, ref int bestPriority)
    {
        for (var i = 0; i < WindowCount; i++)
        {
            if (!IsWindowSlotActive(i))
            {
                continue;
            }

            Consider($"window {i}", AddressFieldWindowTextBuffers + (i * WindowTextBufferStride), priority: 2, ref best, ref bestPriority);
        }
    }

    private bool IsWindowSlotActive(int index) => readByte!(AddressFieldWindowStates + index) != FreeWindowState;

    private static bool IsInsideFieldMessageData(int messageDataPointer, int textPointer) =>
        messageDataPointer > 0 && textPointer > 0 && textPointer >= messageDataPointer && textPointer < messageDataPointer + FieldMessageDataRange;

    private void Consider(string source, int address, int priority, ref FieldMessageCandidate best, ref int bestPriority)
    {
        var text = NormalizeText(address);
        if (text.Length == 0)
        {
            return;
        }

        if (priority > bestPriority || priority == bestPriority && text.Length > best.Text.Length)
        {
            best = new FieldMessageCandidate(source, text);
            bestPriority = priority;
        }
    }

    private string NormalizeText(int address) =>
        Ff7EncodedTextDecoder.NormalizeWhitespace(readText!(address, FieldTextBufferLength) ?? string.Empty);

    private static bool TryAdd(uint address, ulong offset, out uint result)
    {
        var sum = address + offset;
        result = sum <= uint.MaxValue ? (uint)sum : 0;
        return sum <= uint.MaxValue;
    }

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
            Module == other.Module && FieldId == other.FieldId && MessageDataPointer == other.MessageDataPointer &&
            States.AsSpan().SequenceEqual(other.States) && Pointers.AsSpan().SequenceEqual(other.Pointers);
    }
}

public readonly record struct FieldVisibleWindowSnapshot(int WindowId, byte NativeState, string Text, uint GuestPointer);

public sealed class FieldMessageDiagnostics
{
    public FieldMessageDiagnostics(uint messageDataPointer, IReadOnlyList<FieldMessageWindowDiagnostic> windows, string lineBufferText)
    {
        MessageDataPointer = messageDataPointer;
        Windows = windows;
        LineBufferText = lineBufferText;
    }

    public uint MessageDataPointer { get; }
    public IReadOnlyList<FieldMessageWindowDiagnostic> Windows { get; }
    public string LineBufferText { get; }

    public string ToCompactLogLine()
    {
        var builder = new StringBuilder();
        builder.Append($"data=0x{MessageDataPointer:X8}");
        if (LineBufferText.Length != 0) builder.Append($" line=\"{Preview(LineBufferText)}\"");
        var wroteWindow = false;
        foreach (var window in Windows)
        {
            if (!window.ShouldLog) continue;
            builder.Append(" ");
            builder.Append($"slot{window.Index}:state=0x{window.State:X2}");
            builder.Append($",state32=0x{window.StateInt32:X8}");
            builder.Append($",ptr=0x{window.Pointer:X8}");
            builder.Append($",inRange={window.PointerInsideMessageData}");
            if (window.PointerText.Length != 0) builder.Append($",ptrText=\"{Preview(window.PointerText)}\"");
            if (window.BufferText.Length != 0) builder.Append($",bufText=\"{Preview(window.BufferText)}\"");
            wroteWindow = true;
        }

        if (!wroteWindow) builder.Append(" windows=<empty>");
        return builder.ToString();
    }

    private static string Preview(string text)
    {
        var normalized = text.Replace("\"", "'", StringComparison.Ordinal);
        return normalized.Length <= 72 ? normalized : normalized[..72] + "...";
    }
}

public readonly record struct FieldMessageWindowDiagnostic(
    int Index,
    byte State,
    int StateInt32,
    uint Pointer,
    bool PointerInsideMessageData,
    string PointerText,
    uint BufferAddress,
    string BufferText)
{
    public bool IsActive => State != 0xff;
    public bool ShouldLog => IsActive || Pointer != 0 || PointerText.Length != 0 || BufferText.Length != 0;
}
