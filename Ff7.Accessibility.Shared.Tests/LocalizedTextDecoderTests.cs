using Ff7.Accessibility.Reloaded;

internal static class LocalizedTextDecoderTests
{
    public static void Run()
    {
        DecodesWesternExtendedCharacters();
        DecodesJapaneseKanaAndKanji();
        PreservesFieldPagesChoicesAndLocalizedText();
        RepresentsUnknownCodesInsteadOfDroppingTheLine();
    }

    private static void DecodesWesternExtendedCharacters()
    {
        Equal(
            "Guéri",
            Ff7EncodedTextDecoder.DecodeKernelTerminated(
                new byte[] { 0x27, 0x55, 0x6e, 0x52, 0x49, 0xff },
                Ff7GameLanguages.Get(Ff7GameLanguage.French)),
            "French kernel accents");
        Equal(
            "Ärger",
            Ff7EncodedTextDecoder.DecodeFieldTerminated(
                new byte[] { 0x60, 0x52, 0x47, 0x45, 0x52, 0xff },
                Ff7GameLanguages.Get(Ff7GameLanguage.German)),
            "German field accents");
    }

    private static void DecodesJapaneseKanaAndKanji()
    {
        var japanese = Ff7GameLanguages.Get(Ff7GameLanguage.Japanese);
        Equal(
            "ポーション",
            Ff7EncodedTextDecoder.DecodeKernelTerminated(
                new byte[] { 0x31, 0xd0, 0x56, 0xa2, 0x98, 0xff },
                japanese),
            "Japanese kana");
        Equal(
            "経験値",
            Ff7EncodedTextDecoder.DecodeKernelTerminated(
                new byte[] { 0xfb, 0xd7, 0xfb, 0xd8, 0xfb, 0xd9, 0xff },
                japanese),
            "Japanese multibyte kanji");
        Equal(
            "バレット",
            Ff7EncodedTextDecoder.DecodeFieldTerminated(
                new byte[] { 0x00, 0x8c, 0x9c, 0x66, 0xff },
                japanese),
            "Japanese field text");
    }

    private static void PreservesFieldPagesChoicesAndLocalizedText()
    {
        var french = Ff7GameLanguages.Get(Ff7GameLanguage.French);
        var pages = Ff7EncodedTextDecoder.DecodeFieldPages(
            new byte[]
            {
                0xe0, 0x27, 0x55, 0x6e, 0x52, 0x49,
                0xe7,
                0x60,
                0xe8,
                0x22, 0x4f, 0x4e, 0x4a, 0x4f, 0x55, 0x52,
                0xff
            },
            french);

        Equal(2, pages.Count, "page count");
        Equal(true, pages[0].Lines[0].IsChoice, "choice marker");
        Equal("Guéri", pages[0].Lines[0].Text, "localized choice");
        Equal("Ä", pages[0].Lines[1].Text, "localized second line");
        Equal("Bonjour", pages[1].Lines[0].Text, "second page");
    }

    private static void RepresentsUnknownCodesInsteadOfDroppingTheLine()
    {
        var english = Ff7GameLanguages.Get(Ff7GameLanguage.English);
        Equal(
            "A�B",
            Ff7EncodedTextDecoder.DecodeFieldTerminated(
                new byte[] { 0x21, 0xf3, 0x22, 0xff },
                english),
            "unknown field code replacement");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }
}
