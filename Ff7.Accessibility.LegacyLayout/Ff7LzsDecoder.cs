namespace Ff7.Accessibility.Reloaded;

public static class Ff7LzsDecoder
{
    private const int SlidingWindowSize = 4096;
    private const int InitialWindowPosition = 0xFEE;

    public static byte[] DecodeFieldFile(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < sizeof(int))
        {
            return [];
        }

        var declaredLength = BitConverter.ToInt32(bytes[..sizeof(int)]);
        var input = declaredLength == bytes.Length - sizeof(int)
            ? bytes[sizeof(int)..]
            : bytes;
        return Decode(input);
    }

    public static byte[] Decode(ReadOnlySpan<byte> input)
    {
        var output = new List<byte>(input.Length * 2);
        var window = new byte[SlidingWindowSize];
        var writeIndex = InitialWindowPosition;
        var inputIndex = 0;

        while (inputIndex < input.Length)
        {
            var flags = input[inputIndex++];
            for (var bit = 0; bit < 8 && inputIndex < input.Length; bit++)
            {
                if ((flags & (1 << bit)) != 0)
                {
                    var value = input[inputIndex++];
                    output.Add(value);
                    window[writeIndex] = value;
                    writeIndex = (writeIndex + 1) & 0xFFF;
                    continue;
                }

                if (inputIndex + 1 >= input.Length)
                {
                    break;
                }

                var low = input[inputIndex++];
                var high = input[inputIndex++];
                var readIndex = low | ((high & 0xF0) << 4);
                var length = (high & 0x0F) + 3;
                for (var i = 0; i < length; i++)
                {
                    var value = window[(readIndex + i) & 0xFFF];
                    output.Add(value);
                    window[writeIndex] = value;
                    writeIndex = (writeIndex + 1) & 0xFFF;
                }
            }
        }

        return output.ToArray();
    }
}
