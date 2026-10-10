using Ff7.Accessibility.Reloaded;

internal static class FieldButtonGlyphTests
{
    // Installed flevel.lgp, mtcrl_4 (field 462), dialog 5. Keep the native line
    // breaks and bytes: the old decoder spoke "[OK]9/[OK]:" and "[OK]0".
    private static readonly byte[] NativeTreasurePrompt = Convert.FromHexString(
        "2745540054484500545245415355524500425900484F4C44494E47E700F6190FF61A00444F574E" +
        "E7005748494C450052455045415445444C59005052455353494E4700F61001FF");

    // Installed flevel.lgp (byte-identical in the legacy and Steam 2026 archives),
    // niv_ti2 (field 287) dialogs 35 and 36: Tifa's piano sheet and its "I remember"
    // reminder. The old decoder spoke "[OK]1/[OK]3", "[OK]8", "[OK]7" and "Use [OK]5 to
    // end.", so a player who could not see the glyphs never learned that Start ends it.
    private static readonly byte[] NativePianoSheet = Convert.FromHexString(
        "244F00F9E7324500F8E72D4900F7E7264100F610E7334F00F6110FF613000B00F9E72C4100F6110FF6" +
        "13000B00F8E7344900F6110FF613000B00F7E7244F00F6110FF613000B00F610E7244F002D4900334F" +
        "00082309E1F618E7244F002641002C4100082609E1F619E7324500334F00344900082709E1F617E72D" +
        "4900334F00244F00082309E1F61AE7254E44E1E1F615FF");

    private static readonly byte[] NativePianoReminder = Convert.FromHexString(
        "000035534500F61500544F00454E440EFF");

    // sinin1_2 (field 298) dialogs 20 and 21, byte-identical in both archives: the Shinra
    // Mansion piano's sheet and reminder. The same buttons, with "or" between the shoulders.
    private static readonly byte[] NativeMansionPianoSheet = Convert.FromHexString(
        "244F00F9E7324500F8E72D4900F7E7264100F610E7334F00F611004F5200F613000B00F9E72C4100F611" +
        "004F5200F613000B00F8E7344900F611004F5200F613000B00F7E7244F00F611004F5200F613000B00F6" +
        "10E7244F002D4900334F0000082309E1F618E7244F002641002C41000000082609E1F619E7324500334F" +
        "003449000000082709E1F617E72D4900334F00244F0000082309E1F61AE7254E44E1E1F615FF");

    private static readonly byte[] NativeMansionPianoReminder = Convert.FromHexString("0000F615E1E1254E44FF");

    internal static void Run()
    {
        DecodesInstalledMountCorelInstructions();
        DecodesTheNativePianoInstructions();
        DecodesEveryVerifiedPairedButton();
        PreservesNativePromptLinesAndTerminatorRequirements();
        PreservesStandaloneButtonsAndUnverifiedFollowingBytes();
        PreservesJapaneseCharactersFollowingLegacyButtons();
    }

    private static void DecodesTheNativePianoInstructions()
    {
        foreach (var language in Ff7GameLanguages.All.Where(value => !value.UsesJapaneseEncoding))
        {
            Equal("Use [START] to end.", Ff7EncodedTextDecoder.DecodeFieldTerminated(NativePianoReminder, language),
                $"the piano's own end instruction names Start ({language.Code})");
        }

        var english = Ff7GameLanguages.Get(Ff7GameLanguage.English);
        var lines = Ff7EncodedTextDecoder.DecodeFieldLines(NativePianoSheet, english);
        Equal(string.Join(" | ",
            "Do [CANCEL]", "Re [SWITCH]", "Mi [MENU]", "Fa [OK]",
            "So [L1]/[R1] + [CANCEL]", "La [L1]/[R1] + [SWITCH]", "Ti [L1]/[R1] + [MENU]", "Do [L1]/[R1] + [OK]",
            "Do Mi So (C) [DOWN]", "Do Fa La (F) [LEFT]", "Re So Ti (G) [UP]", "Mi So Do (C) [RIGHT]",
            "End [START]"),
            string.Join(" | ", lines),
            "every line of the piano sheet names the button the script reads");

        Equal(string.Join(" | ",
            "Do [CANCEL]", "Re [SWITCH]", "Mi [MENU]", "Fa [OK]",
            "So [L1] or [R1] + [CANCEL]", "La [L1] or [R1] + [SWITCH]", "Ti [L1] or [R1] + [MENU]", "Do [L1] or [R1] + [OK]",
            "Do Mi So (C) [DOWN]", "Do Fa La (F) [LEFT]", "Re So Ti (G) [UP]", "Mi So Do (C) [RIGHT]",
            "End [START]"),
            string.Join(" | ", Ff7EncodedTextDecoder.DecodeFieldLines(NativeMansionPianoSheet, english)),
            "every line of the Shinra Mansion piano sheet names the button its script reads");
        Equal("[START] End", Ff7EncodedTextDecoder.DecodeFieldTerminated(NativeMansionPianoReminder, english),
            "the Shinra Mansion piano's own end reminder names Start");
    }

    private static void DecodesEveryVerifiedPairedButton()
    {
        // Cross-checked three ways: niv_ti2's own key tests (L1|R1 0x000C held for the upper
        // octave, D-pad Down/Left/Up/Right for the C/F/G/C chords, Start 0x0800 to end), FFNx's
        // universal_buttons_parse_field_prompt (codes 0x10-0x1A and the same order again at
        // 0x33-0x3D), and the Steam 2026 renderer, which draws 0x33 with the legacy OK glyph.
        var english = Ff7GameLanguages.Get(Ff7GameLanguage.English);
        string[] names = ["[OK]", "[L1]", "[L2]", "[R1]", "[R2]", "[START]", "[SELECT]", "[UP]", "[DOWN]", "[LEFT]", "[RIGHT]"];
        foreach (var start in new byte[] { 0x10, 0x33 })
        {
            for (var offset = 0; offset < names.Length; offset++)
            {
                Equal($"Press {names[offset]} now",
                    Ff7EncodedTextDecoder.DecodeFieldTerminated(
                        [0x30, 0x52, 0x45, 0x53, 0x53, 0x00, 0xF6, (byte)(start + offset), 0x00, 0x4E, 0x4F, 0x57, 0xFF], english),
                    $"paired button 0x{start + offset:X2}");
            }
        }
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
        Equal("[OK]; [OK]A [OK]/ [OK]R [OK]^ [MENU]9 [SWITCH]: [CANCEL]0",
            Ff7EncodedTextDecoder.DecodeFieldTerminated(
                [0xF6, 0x1B, 0, 0xF6, 0x21, 0, 0xF6, 0x0F, 0, 0xF6, 0x32, 0, 0xF6, 0x3E, 0,
                 0xF7, 0x19, 0, 0xF8, 0x1A, 0, 0xF9, 0x10, 0xFF], english),
            "unverified prefix/index combinations, either side of both verified ranges, retain their original text");
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
        foreach (var code in new byte[] { 0x11, 0x15, 0x17, 0x18, 0x34, 0x38 })
        {
            var character = Ff7EncodedTextDecoder.DecodeFieldTerminated([code, 0xFF], japanese);
            Equal("[OK]" + character,
                Ff7EncodedTextDecoder.DecodeFieldTerminated([0xF6, code, 0xFF], japanese),
                $"Western button code 0x{code:X2} remains an ordinary Japanese character");
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }
}
