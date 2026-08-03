using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

internal static class LegacyFf7TextReader
{
    public static bool TryReadTerminated(
        ILegacyAddressSpace addressSpace,
        uint address,
        int maximumLength,
        out byte[] bytes,
        out string text)
    {
        bytes = [];
        text = string.Empty;
        if (address == 0 || maximumLength <= 0)
        {
            return false;
        }

        var values = new List<byte>(maximumLength);
        var offset = 0;
        while (offset < maximumLength)
        {
            if (!TryAdd(address, (uint)offset, out var chunkAddress))
            {
                return false;
            }

            var pageRemaining = 0x1000 - (int)(chunkAddress & 0xfff);
            var chunkLength = Math.Min(pageRemaining, maximumLength - offset);
            var chunk = new byte[chunkLength];
            if (!addressSpace.TryRead(chunkAddress, chunk))
            {
                return false;
            }

            var terminator = chunk.AsSpan().IndexOf((byte)0xff);
            var used = terminator >= 0 ? terminator + 1 : chunk.Length;
            values.AddRange(chunk.AsSpan(0, used).ToArray());
            offset += used;
            if (terminator >= 0)
            {
                bytes = values.ToArray();
                text = Ff7EncodedTextDecoder.Decode(bytes);
                return true;
            }
        }

        return false;
    }

    public static bool TryAdd(uint left, uint right, out uint result)
    {
        var sum = (ulong)left + right;
        result = sum <= uint.MaxValue ? (uint)sum : 0;
        return sum <= uint.MaxValue;
    }
}
