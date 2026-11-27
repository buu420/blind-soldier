using Ff7.Accessibility.Reloaded;

internal static class RussianTextSupportTests
{
    public static void Run()
    {
        if (!Ff7GameLanguages.TryParse("ru", out var russian))
            throw new InvalidOperationException("The FFRTT Russian translation needs its own text profile.");
        Equal(Ff7GameLanguage.English, russian.Language, "Russian retains the English engine layout");
        Equal("lang-en", russian.LanguageDirectoryName, "Russian overlay data directory");
        Equal("flevel.lgp", russian.FieldArchiveName, "Russian field overlay");
        Equal(5, Ff7GameLanguages.All.Count, "official languages stay distinct from fan profiles");
        Equal("Сефирот", Ff7EncodedTextDecoder.DecodeKernelTerminated(
            [0x23, 0x6c, 0x79, 0x6f, 0x50, 0x4f, 0x78, 0xff], russian), "patched character name");
        Equal("Призыв", Ff7EncodedTextDecoder.DecodeKernelTerminated(
            [0x03, 0x50, 0x6f, 0x6d, 0x7e, 0x69, 0xff], russian), "loaded command name");
        Equal("Зелье", Ff7EncodedTextDecoder.DecodeKernelTerminated(
            [0x13, 0x6c, 0x71, 0x7f, 0x6c, 0xff], russian), "shared digit-three glyph in an item word");
        Equal("Лечить3", Ff7EncodedTextDecoder.DecodeKernelTerminated(
            [0xc8, 0x6c, 0x1d, 0x6f, 0x78, 0x7f, 0x13, 0xff], russian), "spell level remains a digit");
        Equal("123", Ff7EncodedTextDecoder.DecodeKernelTerminated(
            [0x11, 0x12, 0x13, 0xff], russian), "numeric values remain digits");
        Equal("МЫ", Ff7EncodedTextDecoder.DecodeFieldTerminated([0x2d, 0xd3, 0xff], russian),
            "native D3 renderer alias in Russian field dialogue");
        Equal("Чоко Билли", Ff7EncodedTextDecoder.DecodeFieldTerminated(
            [0x14, 0x4f, 0x70, 0x4f, 0x00, 0x20, 0x6f, 0x71, 0x71, 0x6f, 0xff], russian),
            "shared Che glyph in the native Choco Billy heading");
        Equal("Уров.:", Ff7EncodedTextDecoder.DecodeKernelTerminated(
            [0xce, 0x50, 0x4f, 0x69, 0x72, 0x1a, 0xff], russian), "Russian battle level punctuation");
        Equal("Final Fantasy VII", Ff7EncodedTextDecoder.DecodeKernelTerminated(
            [0x26, 0x49, 0x4e, 0x41, 0x4c, 0x00, 0x26, 0x41, 0x4e, 0x54, 0x41, 0x53, 0x59,
             0x00, 0x36, 0x29, 0x29, 0xff], russian), "retained Latin title");
        var pages = Ff7EncodedTextDecoder.DecodeFieldPages(
            [0xe0, 0x2d, 0x41, 0x6b, 0x6f, 0xb8, 0xe8, 0x23, 0x6c, 0x79, 0x6f, 0x50, 0x4f, 0x78, 0xff], russian);
        Equal(2, pages.Count, "Russian page control");
        Equal(true, pages[0].Lines[0].IsChoice, "Russian choice control");
        Equal("Магия", pages[0].Lines[0].Text, "Russian choice text");
        Equal("Сефирот", pages[1].Lines[0].Text, "Russian second page");
        Equal("[Вверх] [Подтвердить]", Ff7EncodedTextDecoder.DecodeFieldTerminated(
            [0xf6, 0x17, 0x00, 0xf6, 0x10, 0xff], russian), "localized native button tokens preserve their controls");
        Equal("#", Ff7EncodedTextDecoder.DecodeKernelTerminated([0x03, 0xff],
            Ff7GameLanguages.Get(Ff7GameLanguage.English)), "Russian glyph does not affect English");
        DetectsOnlyTheActiveCoherentLegacyTranslation();
        ReadsTheLoadedRussianFieldTableInsteadOfAnEnglishDiskFallback(russian);
        LocalizesComposedModSpeechWithoutBroadeningNativeDialoguePatterns(russian);
    }

    private static void LocalizesComposedModSpeechWithoutBroadeningNativeDialoguePatterns(Ff7GameLanguageDescriptor russian)
    {
        var localizer = BlindSoldierLocalizer.CreateForTesting(russian, new Dictionary<string, string>(),
            new Dictionary<string, string>
            {
                ["Mod settings."] = "Настройки мода.", ["Navigation"] = "Навигация",
                ["Auto walk"] = "Автоходьба", ["on"] = "включено", ["{0} of {1}."] = "{0} из {1}.",
                ["Press {0} twice to {1}."] = "Для {1} дважды нажмите {0}.", ["reset descriptions"] = "сброса описаний"
            });
        Equal("Настройки мода. Навигация: Автоходьба, включено. 1 из 80.",
            localizer.LocalizeModSpeech("Mod settings. Navigation: Auto walk, on. 1 of 80."), "composed mod settings");
        Equal("Для сброса описаний дважды нажмите I.",
            localizer.LocalizeModSpeech("Press I twice to reset descriptions."), "nested mod-owned action caption");
        Equal("Words of wisdom.", localizer.Localize("Words of wisdom."), "a count template cannot reinterpret native dialogue");
        Equal("Cloud: Lifestream.", localizer.Localize("Cloud: Lifestream."), "ordinary dialogue stays outside composition translation");
    }

    private static void ReadsTheLoadedRussianFieldTableInsteadOfAnEnglishDiskFallback(Ff7GameLanguageDescriptor russian)
    {
        var memory = new ContiguousLegacyAddressSpace();
        memory.Write((uint)FieldPositionReader.AddressCurrentModule, new byte[] { FieldPositionReader.FieldModule });
        memory.Write((uint)FieldPositionReader.AddressFieldId, BitConverter.GetBytes((ushort)105));
        memory.Write(FieldMessageReader.AddressFieldMessageDataPointer, BitConverter.GetBytes(0x01000000u));
        memory.Write(FieldMessageReader.AddressFieldWindowStates, new byte[] { 255, 255, 255, 255 });
        memory.Write(FieldMessageReader.AddressFieldWindowMessagePointers, new byte[16]);
        memory.Write(0x01000000, new byte[0x1000]);
        memory.Write(0x01000000, new byte[] { 1, 0, 4, 0, 0x03, 0x50, 0x6f, 0x6d, 0x7e, 0x69, 0xff });
        var missingRoot = Path.Combine(Path.GetTempPath(), "blind-soldier-missing-archive");
        var context = new Ff7GameLanguageContext(russian, Path.Combine(missingRoot, "data"),
            Ff7GameLanguageDetectionSource.Configuration, "Russian loaded text test");
        var resolver = new FlevelFieldTextResolver(missingRoot, context);
        resolver.UseNativeText(memory);
        Ff7EncodedTextDecoder.SetDefaultLanguage(russian);
        try
        {
            Equal("Призыв", resolver.ReadMessageById(105, 0).Text, "loaded field text without a Russian physical archive");
            Equal("Призыв", resolver.ReadMessageLinesById(105, 0).Single(), "loaded NPC dialogue heading lines");
            Equal("Призыв", resolver.ReadMessagePagesById(105, 0).Single().Lines.Single().Text, "loaded choice pages");
            Equal(false, new FieldMessageReader(memory).TryReadMessagePagesById(106, 0, out _), "native message snapshot enforces its requested field owner");
            Equal(string.Empty, resolver.ReadMessageById(106, 0).Text, "another field must not borrow the current table");
            var nameAddress = (uint)(SavemapPartyReader.AddressSavemap + SavemapPartyReader.CharactersOffset + SavemapPartyReader.CharacterNameOffset);
            memory.Write(nameAddress, new byte[] { 0x23, 0x6c, 0x79, 0x6f, 0x50, 0x4f, 0x78, 0xff, 0xff, 0xff, 0xff, 0xff });
            memory.Write(0x01000000, new byte[] { 1, 0, 4, 0, 0xea, 0xff });
            Equal("Сефирот", resolver.ReadMessagePagesById(105, 0).Single().Lines.Single().Text,
                "canonical character tokens use the actual saved Russian name");
            Equal("Cloud", resolver.ReadMessageLinesById(105, 0).Single(),
                "NPC label classification retains canonical party identity despite a renamed player");
            memory.Write(0x01000000, new byte[] { 0, 0 });
            Equal(string.Empty, resolver.ReadMessageById(105, 0).Text, "unavailable loaded entry cannot reuse stale text");
        }
        finally { Ff7EncodedTextDecoder.SetDefaultLanguage(Ff7GameLanguages.Get(Ff7GameLanguage.English)); }
    }

    private static void DetectsOnlyTheActiveCoherentLegacyTranslation()
    {
        var root = Path.Combine(Path.GetTempPath(), "blind-soldier-russian-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var kernel = Path.Combine(root, "data", "lang-en", "kernel");
            Directory.CreateDirectory(kernel);
            File.WriteAllBytes(Path.Combine(kernel, "kernel2.bin"), [0]);
            var memory = new ContiguousLegacyAddressSpace();
            memory.Write(0x0091a8d4, new byte[] { 0x2d, 0x41, 0x6b, 0x6f, 0xb8, 0xff });
            memory.Write(0x00922430, new byte[] { 0x23, 0x6c, 0x79, 0x6f, 0x50, 0x4f, 0x78, 0xff });
            Ff7GameLanguageContext Detect(string configured = "auto", bool ffnx = true,
                Ff7.Accessibility.LegacyLayout.ILegacyAddressSpace? observed = null, string executable = "ff7_en.exe") =>
                Ff7GameLanguageDetector.Detect(root, configured, Path.Combine(root, executable),
                    Array.Empty<string>(), ffnxLoaded: ffnx, liveLegacyMemory: observed);

            Equal("ru", Detect(observed: memory).Code, "active Russian Hext overlay");
            Equal(Ff7GameLanguageDetectionSource.TranslationFingerprint, Detect(observed: memory).Source, "active fingerprint source");
            Equal("en", Detect().Code, "an installed archive alone does not activate Russian");
            Equal("en", Detect("en", observed: memory).Code, "explicit English wins");
            Equal("ru", Detect("ru", ffnx: false).Code, "explicit Russian override");
            Equal("ru", Detect("ru-RU", ffnx: false).Code, "Russian culture-code override");
            Equal("en", Detect(ffnx: false, observed: memory).Code, "inactive legacy driver");
            Equal("en", Detect(observed: memory, executable: "FFVII.exe").Code, "never probe native x64 addresses");
            var partial = new ContiguousLegacyAddressSpace();
            partial.Write(0x0091a8d4, new byte[] { 0x2d, 0x41, 0x6b, 0x6f, 0xb8, 0xff });
            Equal("en", Detect(observed: partial).Code, "one matching string is not enough");
            var torn = new TearingLegacyAddressSpace(memory, 0x00922430, new byte[8]);
            Equal("en", Detect(observed: torn).Code, "changed native observation is rejected");
        }
        finally
        {
            var intendedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "blind-soldier-russian-tests")) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(intendedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing test cleanup outside its temporary directory.");
            Directory.Delete(root, recursive: true);
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
    }
}
