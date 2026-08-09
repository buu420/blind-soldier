using System.Text;

namespace Ff7.Accessibility.Reloaded;

public readonly record struct Ff7DecodedTextLine(string Text, bool IsChoice);

public sealed record Ff7DecodedTextPage(IReadOnlyList<Ff7DecodedTextLine> Lines);

public static class Ff7EncodedTextDecoder
{
    private static readonly string[] PartyNames =
    [
        "Cloud", "Barret", "Tifa", "Aerith", "Red XIII",
        "Yuffie", "Cait Sith", "Vincent", "Cid"
    ];

    private static Ff7GameLanguageDescriptor English =>
        Ff7GameLanguages.Get(Ff7GameLanguage.English);

    // Compatibility entry points. Existing memory readers contain field text.
    public static string Decode(ReadOnlySpan<byte> bytes) => DecodeField(bytes, English);

    public static string DecodeTerminated(ReadOnlySpan<byte> bytes) =>
        DecodeFieldTerminated(bytes, English);

    public static IReadOnlyList<string> DecodeLines(ReadOnlySpan<byte> bytes) =>
        DecodeFieldLines(bytes, English);

    public static IReadOnlyList<Ff7DecodedTextPage> DecodePages(ReadOnlySpan<byte> bytes) =>
        DecodeFieldPages(bytes, English);

    public static string DecodeField(ReadOnlySpan<byte> bytes, Ff7GameLanguageDescriptor language)
    {
        var pages = DecodeFieldPagesCore(bytes, language, requireTerminator: false);
        return NormalizeWhitespace(string.Join(
            " ",
            pages.SelectMany(page => page.Lines).Select(line => line.Text)));
    }

    public static string DecodeFieldTerminated(ReadOnlySpan<byte> bytes, Ff7GameLanguageDescriptor language)
    {
        var terminatorIndex = bytes.IndexOf((byte)0xff);
        return terminatorIndex < 0
            ? string.Empty
            : DecodeField(bytes[..(terminatorIndex + 1)], language);
    }

    public static IReadOnlyList<string> DecodeFieldLines(
        ReadOnlySpan<byte> bytes,
        Ff7GameLanguageDescriptor language)
    {
        var pages = DecodeFieldPages(bytes, language);
        return pages.Count == 0
            ? Array.Empty<string>()
            : pages.SelectMany(page => page.Lines).Select(line => line.Text).ToArray();
    }

    public static IReadOnlyList<Ff7DecodedTextPage> DecodeFieldPages(
        ReadOnlySpan<byte> bytes,
        Ff7GameLanguageDescriptor language) =>
        DecodeFieldPagesCore(bytes, language, requireTerminator: true);

    public static string DecodeKernel(ReadOnlySpan<byte> bytes, Ff7GameLanguageDescriptor language)
    {
        var builder = new StringBuilder(bytes.Length);
        var japanese = language.UsesJapaneseEncoding;
        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            if (value == 0xff)
            {
                break;
            }

            if (value < 0xe7)
            {
                AppendNormal(builder, value, japanese);
                continue;
            }

            if (value is >= 0xea and <= 0xf0)
            {
                // Runtime variable values are unavailable to an offline reader.
                index = Math.Min(bytes.Length - 1, index + 2);
                continue;
            }

            if (value == 0xf8)
            {
                index = Math.Min(bytes.Length - 1, index + 1);
                continue;
            }

            if (japanese && value is >= 0xfa and <= 0xfe && index + 1 < bytes.Length)
            {
                AppendKanji(builder, value, bytes[++index]);
                continue;
            }

            builder.Append('\uFFFD');
        }

        return NormalizeWhitespace(builder.ToString());
    }

    public static string DecodeKernelTerminated(
        ReadOnlySpan<byte> bytes,
        Ff7GameLanguageDescriptor language)
    {
        var terminatorIndex = bytes.IndexOf((byte)0xff);
        return terminatorIndex < 0
            ? string.Empty
            : DecodeKernel(bytes[..(terminatorIndex + 1)], language);
    }

    private static IReadOnlyList<Ff7DecodedTextPage> DecodeFieldPagesCore(
        ReadOnlySpan<byte> bytes,
        Ff7GameLanguageDescriptor language,
        bool requireTerminator)
    {
        var pages = new List<Ff7DecodedTextPage>();
        var lines = new List<Ff7DecodedTextLine>();
        var builder = new StringBuilder(bytes.Length);
        var isChoice = false;
        var terminated = false;
        var japanese = language.UsesJapaneseEncoding;

        void FinishLine()
        {
            lines.Add(new Ff7DecodedTextLine(NormalizeWhitespace(builder.ToString()), isChoice));
            builder.Clear();
            isChoice = false;
        }

        void FinishPage()
        {
            FinishLine();
            pages.Add(new Ff7DecodedTextPage(lines.ToArray()));
            lines.Clear();
        }

        for (var index = 0; index < bytes.Length; index++)
        {
            var value = bytes[index];
            if (value == 0xff)
            {
                FinishPage();
                terminated = true;
                break;
            }

            var normalLimit = japanese ? 0xe7 : 0xe0;
            if (value < normalLimit)
            {
                AppendNormal(builder, value, japanese);
                continue;
            }

            if (japanese && value is >= 0xfa and <= 0xfd)
            {
                if (index + 1 < bytes.Length)
                {
                    AppendKanji(builder, value, bytes[++index]);
                }
                else
                {
                    builder.Append('\uFFFD');
                }

                continue;
            }

            switch (value)
            {
                case 0xe0 when !japanese:
                    if (builder.Length == 0)
                    {
                        isChoice = true;
                    }
                    break;
                case 0xe1 when !japanese:
                    builder.Append(' ');
                    break;
                case 0xe2 when !japanese:
                    builder.Append(", ");
                    break;
                case 0xe3 when !japanese:
                    builder.Append(".\"");
                    break;
                case 0xe4 when !japanese:
                    builder.Append("…\"");
                    break;
                case 0xe6:
                    builder.Append('⑬');
                    break;
                case 0xe7:
                    FinishLine();
                    break;
                case 0xe8:
                    FinishPage();
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
                    if (index + 1 >= bytes.Length)
                    {
                        builder.Append('\uFFFD');
                        break;
                    }

                    var subcode = bytes[++index];
                    if (japanese && subcode < 0xd2)
                    {
                        AppendKanji(builder, 0xfe, subcode);
                    }
                    else
                    {
                        SkipExtendedControl(bytes, ref index, subcode);
                    }
                    break;
                default:
                    builder.Append('\uFFFD');
                    break;
            }
        }

        if (!terminated && !requireTerminator)
        {
            FinishPage();
        }

        return terminated || !requireTerminator
            ? pages
            : Array.Empty<Ff7DecodedTextPage>();
    }

    private static void AppendNormal(StringBuilder builder, byte value, bool japanese)
    {
        if (Ff7TextEncoding.TryReadNormal(value, japanese, out var character))
        {
            builder.Append(character);
        }
        else
        {
            builder.Append('\uFFFD');
        }
    }

    private static void AppendKanji(StringBuilder builder, byte bank, byte code)
    {
        if (Ff7TextEncoding.TryReadKanji(bank, code, out var character))
        {
            builder.Append(character);
        }
        else
        {
            builder.Append('\uFFFD');
        }
    }

    private static void SkipExtendedControl(ReadOnlySpan<byte> bytes, ref int index, byte subcode)
    {
        var argumentLength = subcode switch
        {
            0xdd => 2,
            0xe2 => 4,
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
