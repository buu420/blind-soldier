using System.Text;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct Ff7DecodedTextLine(string Text, bool IsChoice);

public sealed record Ff7DecodedTextPage(IReadOnlyList<Ff7DecodedTextLine> Lines);

public static class Ff7EncodedTextDecoder
{
    private static readonly string[] PartyNames =
    [
        "Cloud",
        "Barret",
        "Tifa",
        "Aerith",
        "Red XIII",
        "Yuffie",
        "Cait Sith",
        "Vincent",
        "Cid"
    ];

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        for (var i = 0; i < bytes.Length; i++)
        {
            var value = bytes[i];
            if (value == 0xff)
            {
                break;
            }

            if (value <= 0x5e)
            {
                builder.Append((char)(value + 0x20));
                continue;
            }

            switch (value)
            {
                case 0xa9:
                    builder.Append("... ");
                    break;
                case 0xd0:
                case 0xe1:
                    builder.Append(' ');
                    break;
                case 0xe2:
                    builder.Append(", ");
                    break;
                case 0xe3:
                    builder.Append(". ");
                    break;
                case 0xe4:
                    builder.Append("... ");
                    break;
                case 0xe7:
                case 0xe8:
                case 0xe9:
                    builder.Append(' ');
                    break;
                case >= 0xea and <= 0xf2:
                    builder.Append(PartyNames[value - 0xea]);
                    break;
                case 0xf6:
                    builder.Append("[OK]");
                    break;
                case 0xf7:
                    builder.Append("[MENU]");
                    break;
                case 0xf8:
                    builder.Append("[SWITCH]");
                    break;
                case 0xf9:
                    builder.Append("[CANCEL]");
                    break;
                case 0xfe:
                    if (i + 1 < bytes.Length)
                    {
                        i++;
                    }
                    break;
            }
        }

        return NormalizeWhitespace(builder.ToString());
    }

    public static string DecodeTerminated(ReadOnlySpan<byte> bytes)
    {
        var terminatorIndex = bytes.IndexOf((byte)0xff);
        return terminatorIndex < 0
            ? string.Empty
            : Decode(bytes[..(terminatorIndex + 1)]);
    }

    public static IReadOnlyList<string> DecodeLines(ReadOnlySpan<byte> bytes)
    {
        var pages = DecodePages(bytes);
        return pages.Count == 0
            ? Array.Empty<string>()
            : pages
                .SelectMany(page => page.Lines)
                .Select(line => line.Text)
                .ToArray();
    }

    public static IReadOnlyList<Ff7DecodedTextPage> DecodePages(ReadOnlySpan<byte> bytes)
    {
        var pages = new List<Ff7DecodedTextPage>();
        var lines = new List<Ff7DecodedTextLine>();
        var builder = new StringBuilder(bytes.Length);
        var isChoice = false;

        void FinishLine()
        {
            lines.Add(new Ff7DecodedTextLine(
                NormalizeWhitespace(builder.ToString()),
                isChoice));
            builder.Clear();
            isChoice = false;
        }

        void FinishPage()
        {
            FinishLine();
            pages.Add(new Ff7DecodedTextPage(lines.ToArray()));
            lines.Clear();
        }

        for (var i = 0; i < bytes.Length; i++)
        {
            var value = bytes[i];
            if (value == 0xff)
            {
                FinishPage();
                return pages;
            }

            if (value <= 0x5e)
            {
                builder.Append((char)(value + 0x20));
                continue;
            }

            switch (value)
            {
                case 0xa9:
                    builder.Append("... ");
                    break;
                case 0xd0:
                case 0xe1:
                    builder.Append(' ');
                    break;
                case 0xe2:
                    builder.Append(", ");
                    break;
                case 0xe3:
                    builder.Append(". ");
                    break;
                case 0xe4:
                    builder.Append("... ");
                    break;
                case 0xe0:
                    // Choice indentation is visual layout, not spoken text.
                    if (builder.Length == 0)
                    {
                        isChoice = true;
                    }
                    break;
                case 0xe7:
                    FinishLine();
                    break;
                case 0xe8:
                    FinishPage();
                    break;
                case 0xe9:
                    builder.Append(' ');
                    break;
                case >= 0xea and <= 0xf2:
                    builder.Append(PartyNames[value - 0xea]);
                    break;
                case 0xf6:
                    builder.Append("[OK]");
                    break;
                case 0xf7:
                    builder.Append("[MENU]");
                    break;
                case 0xf8:
                    builder.Append("[SWITCH]");
                    break;
                case 0xf9:
                    builder.Append("[CANCEL]");
                    break;
                case 0xfe:
                    SkipExtendedControl(bytes, ref i);
                    break;
            }
        }

        return Array.Empty<Ff7DecodedTextPage>();
    }

    private static void SkipExtendedControl(ReadOnlySpan<byte> bytes, ref int index)
    {
        if (index + 1 >= bytes.Length)
        {
            return;
        }

        var subcode = bytes[++index];
        var argumentLength = subcode switch
        {
            0xdd => 2, // WAIT
            0xe2 => 4, // STR
            _ => 0
        };
        index = Math.Min(bytes.Length - 1, index + argumentLength);
    }

    public static string NormalizeWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousWasWhitespace = true;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                if (!previousWasWhitespace)
                {
                    builder.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }
}
