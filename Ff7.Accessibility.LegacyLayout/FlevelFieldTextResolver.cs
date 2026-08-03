namespace Ff7.Accessibility.Reloaded;

public sealed class FlevelFieldTextResolver
{
    private const int FieldHeaderSectionCountOffset = 2;
    private const int FieldHeaderSectionOffsetsOffset = 6;
    private const int SectionLengthSize = 4;
    private const int ScriptHeaderStringOffsetOffset = 4;

    private readonly FlevelDataSource flevelDataSource;
    private readonly Dictionary<string, IReadOnlyDictionary<int, FieldMessageText>> fieldMessageCache = new(StringComparer.OrdinalIgnoreCase);

    public FlevelFieldTextResolver(string gameRootDirectory)
    {
        flevelDataSource = new FlevelDataSource(gameRootDirectory);
    }

    public FieldMessageCandidate ReadMessageById(int fieldId, int messageId)
    {
        if (fieldId < 0 || messageId < 0)
        {
            return new FieldMessageCandidate(string.Empty, string.Empty);
        }

        var names = GetFieldNames();
        if (!names.TryGetValue(fieldId, out var fieldName) || string.IsNullOrWhiteSpace(fieldName))
        {
            return new FieldMessageCandidate(string.Empty, string.Empty);
        }

        var messages = GetMessages(fieldName);
        if (!messages.TryGetValue(messageId, out var message) || message.Text.Length == 0)
        {
            return new FieldMessageCandidate(string.Empty, string.Empty);
        }

        return new FieldMessageCandidate($"flevel {fieldName} message {messageId}", message.Text);
    }

    public IReadOnlyList<string> ReadMessageLinesById(int fieldId, int messageId)
    {
        if (fieldId < 0 || messageId < 0)
        {
            return Array.Empty<string>();
        }

        var names = GetFieldNames();
        if (!names.TryGetValue(fieldId, out var fieldName) || string.IsNullOrWhiteSpace(fieldName))
        {
            return Array.Empty<string>();
        }

        var messages = GetMessages(fieldName);
        return messages.TryGetValue(messageId, out var message)
            ? message.Lines
            : Array.Empty<string>();
    }

    public IReadOnlyList<Ff7DecodedTextPage> ReadMessagePagesById(int fieldId, int messageId)
    {
        if (fieldId < 0 || messageId < 0)
        {
            return Array.Empty<Ff7DecodedTextPage>();
        }

        var names = GetFieldNames();
        if (!names.TryGetValue(fieldId, out var fieldName) || string.IsNullOrWhiteSpace(fieldName))
        {
            return Array.Empty<Ff7DecodedTextPage>();
        }

        var messages = GetMessages(fieldName);
        return messages.TryGetValue(messageId, out var message)
            ? message.Pages
            : Array.Empty<Ff7DecodedTextPage>();
    }

    private IReadOnlyDictionary<int, string> GetFieldNames()
    {
        return flevelDataSource.FieldNames;
    }

    private IReadOnlyDictionary<int, FieldMessageText> GetMessages(string fieldName)
    {
        if (fieldMessageCache.TryGetValue(fieldName, out var messages))
        {
            return messages;
        }

        messages = flevelDataSource.TryReadField(fieldName, out var fieldBytes)
            ? ReadMessages(fieldBytes)
            : new Dictionary<int, FieldMessageText>();
        fieldMessageCache[fieldName] = messages;
        return messages;
    }

    private static IReadOnlyDictionary<int, FieldMessageText> ReadMessages(byte[] fieldFileBytes)
    {
        var fieldBytes = Ff7LzsDecoder.DecodeFieldFile(fieldFileBytes);
        if (fieldBytes.Length < FieldHeaderSectionOffsetsOffset + sizeof(int))
        {
            return new Dictionary<int, FieldMessageText>();
        }

        var sectionCount = BitConverter.ToInt32(fieldBytes, FieldHeaderSectionCountOffset);
        if (sectionCount <= 0)
        {
            return new Dictionary<int, FieldMessageText>();
        }

        var sectionOneOffset = BitConverter.ToInt32(fieldBytes, FieldHeaderSectionOffsetsOffset);
        var sectionOneDataOffset = sectionOneOffset + SectionLengthSize;
        if (!IsReadable(fieldBytes, sectionOneOffset, SectionLengthSize) ||
            !IsReadable(fieldBytes, sectionOneDataOffset + ScriptHeaderStringOffsetOffset, sizeof(ushort)))
        {
            return new Dictionary<int, FieldMessageText>();
        }

        var sectionOneLength = BitConverter.ToInt32(fieldBytes, sectionOneOffset);
        var sectionOneEnd = sectionOneLength > 0 && sectionOneDataOffset + sectionOneLength <= fieldBytes.Length
            ? sectionOneDataOffset + sectionOneLength
            : fieldBytes.Length;
        var stringOffset = BitConverter.ToUInt16(fieldBytes, sectionOneDataOffset + ScriptHeaderStringOffsetOffset);
        var dialogTableOffset = sectionOneDataOffset + stringOffset;
        if (!IsReadable(fieldBytes, dialogTableOffset, sizeof(ushort)))
        {
            return new Dictionary<int, FieldMessageText>();
        }

        var dialogCount = BitConverter.ToUInt16(fieldBytes, dialogTableOffset);
        var pointerTableOffset = dialogTableOffset + sizeof(ushort);
        if (!IsReadable(fieldBytes, pointerTableOffset, dialogCount * sizeof(ushort)))
        {
            return new Dictionary<int, FieldMessageText>();
        }

        var messages = new Dictionary<int, FieldMessageText>();
        for (var messageId = 0; messageId < dialogCount; messageId++)
        {
            var relativeOffset = BitConverter.ToUInt16(fieldBytes, pointerTableOffset + messageId * sizeof(ushort));
            var textOffset = dialogTableOffset + relativeOffset;
            if (textOffset < pointerTableOffset || textOffset >= sectionOneEnd)
            {
                continue;
            }

            var maxLength = Math.Max(0, sectionOneEnd - textOffset);
            var encodedText = fieldBytes.AsSpan(textOffset, maxLength);
            var text = Ff7EncodedTextDecoder.DecodeTerminated(encodedText);
            if (text.Length != 0)
            {
                var pages = Ff7EncodedTextDecoder.DecodePages(encodedText);
                messages[messageId] = new FieldMessageText(
                    text,
                    pages
                        .SelectMany(page => page.Lines)
                        .Select(line => line.Text)
                        .ToArray(),
                    pages);
            }
        }

        return messages;
    }

    private static bool IsReadable(byte[] bytes, int offset, int length) =>
        offset >= 0 &&
        length >= 0 &&
        offset <= bytes.Length &&
        length <= bytes.Length - offset;

    private readonly record struct FieldMessageText(
        string Text,
        IReadOnlyList<string> Lines,
        IReadOnlyList<Ff7DecodedTextPage> Pages);
}
