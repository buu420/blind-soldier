using Ff7.Accessibility.Reloaded;

internal static class FieldButtonGlyphTests
{
    // Installed flevel.lgp, mtcrl_4 (field 462), dialog 5. Keep the native line
    // breaks and bytes: the old decoder spoke "[OK]9/[OK]:" and "[OK]0".
    private static readonly byte[] NativeTreasurePrompt = Convert.FromHexString(
        "2745540054484500545245415355524500425900484F4C44494E47E700F6190FF61A00444F574E" +
        "E7005748494C450052455045415445444C59005052455353494E4700F61001FF");

    internal static void Run()
    {
        DecodesInstalledMountCorelInstructions();
        PreservesNativePromptLinesAndTerminatorRequirements();
        PreservesStandaloneButtonsAndUnverifiedFollowingBytes();
        PreservesJapaneseCharactersFollowingLegacyButtons();
    }

    private static void DecodesInstalledMountCorelInstructions()
    {
        const string expected =
            "Get the treasure by holding [LEFT]/[RIGHT] down while repeatedly pressing [OK]!";
        foreach (var language in Ff7GameLanguages.All.Where(value => !value.UsesJapaneseEncoding))
        {
            Equal(expected, Ff7EncodedTextDecoder.DecodeFieldTerminated(NativeTreasurePrompt, language),
                $"native Mount Corel treasure directions ({language.Code})");
        }

        var english = Ff7GameLanguages.Get(Ff7GameLanguage.English);
        Equal("Oh, oh oh oh oh!! Press [OK] to jump!",
            Ff7EncodedTextDecoder.DecodeFieldTerminated(Convert.FromHexString(
                "2F48E24F48004F48004F48004F480101E7305245535300F61000544F004A554D5001FF"), english),
            "native Mount Corel jump instruction");
        Equal("[OK] [LEFT] [RIGHT]",
            Ff7EncodedTextDecoder.DecodeField([0xF6, 0x10, 0, 0xF6, 0x19, 0, 0xF6, 0x1A], english),
            "unterminated rendering buffer uses the same button pairs");
    }

    private static void PreservesNativePromptLinesAndTerminatorRequirements()
    {
        var english = Ff7GameLanguages.Get(Ff7GameLanguage.English);
        var pages = Ff7EncodedTextDecoder.DecodeFieldPages(NativeTreasurePrompt, english);
        Equal(1, pages.Count, "native prompt page count");
        Equal(3, pages[0].Lines.Count, "native prompt line count");
        Equal("Get the treasure by holding", pages[0].Lines[0].Text, "native prompt first line");
        Equal("[LEFT]/[RIGHT] down", pages[0].Lines[1].Text, "native prompt direction line");
        Equal("while repeatedly pressing [OK]!", pages[0].Lines[2].Text, "native prompt button line");
        Equal(string.Empty,
            Ff7EncodedTextDecoder.DecodeFieldTerminated(NativeTreasurePrompt.AsSpan(0, NativeTreasurePrompt.Length - 1), english),
            "a stale unterminated message still cannot become speech");
        Equal("[OK]", Ff7EncodedTextDecoder.DecodeFieldTerminated([0xF6, 0xFF, 0x19], english),
            "a button pair cannot consume bytes after the terminator");
    }

    private static void PreservesStandaloneButtonsAndUnverifiedFollowingBytes()
    {
        var english = Ff7GameLanguages.Get(Ff7GameLanguage.English);
        Equal("[OK] [MENU] [SWITCH] [CANCEL]",
            Ff7EncodedTextDecoder.DecodeFieldTerminated([0xF6, 0, 0xF7, 0, 0xF8, 0, 0xF9, 0xFF], english),
            "legacy one-byte buttons retain their following spaces");
        Equal("[OK]1 [OK]A [MENU]9 [SWITCH]: [CANCEL]0",
            Ff7EncodedTextDecoder.DecodeFieldTerminated(
                [0xF6, 0x11, 0, 0xF6, 0x21, 0, 0xF7, 0x19, 0, 0xF8, 0x1A, 0, 0xF9, 0x10, 0xFF], english),
            "unverified prefix/index combinations retain their original text");
        Equal("[OK]", Ff7EncodedTextDecoder.DecodeField([0xF6], english),
            "a trailing legacy button has no second byte to consume");
    }

    private static void PreservesJapaneseCharactersFollowingLegacyButtons()
    {
        var japanese = Ff7GameLanguages.Get(Ff7GameLanguage.Japanese);
        Equal("[OK]ゲ[OK]ず[OK]ゼ",
            Ff7EncodedTextDecoder.DecodeFieldTerminated([0xF6, 0x10, 0xF6, 0x19, 0xF6, 0x1A, 0xFF], japanese),
            "Western button indices remain ordinary Japanese kana");
        Equal("[OK]0",
            Ff7EncodedTextDecoder.DecodeFieldTerminated([0xF6, 0x33, 0xFF], japanese),
            "unverified Japanese button encoding remains unchanged");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }
}
