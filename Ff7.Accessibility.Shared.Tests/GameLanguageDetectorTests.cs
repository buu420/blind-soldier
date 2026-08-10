using Ff7.Accessibility.Reloaded;

internal static class GameLanguageDetectorTests
{
    public static void Run()
    {
        MapsEverySupportedLanguageToItsNativeAssets();
        ParsesPolishFanTranslationProfile();
        RecognizesKnownPolishTranslationFingerprint();
        RecognizesPolishKernelTextWhenTheTranslationFontIsUnavailable();
        ExplicitOverrideWinsWhenItsDataExists();
        ExplicitPolishOverrideUsesEnglishAssetPaths();
        ExecutableSuffixWinsDuringAutomaticDetection();
        MatchingSteamManifestSuppliesTheLanguage();
        AStaleOverrideFallsThroughInsteadOfSelectingMissingData();
        AStaleManifestFallsThroughToTheOnlyUsableLanguage();
        InvalidOverrideFallsThroughToAutomaticDetection();
        EnglishIsTheFinalFallback();
    }

    private static void ParsesPolishFanTranslationProfile()
    {
        Equal(true, Ff7GameLanguages.TryParse("pl", out var descriptor), "Polish language code");
        Equal(Ff7GameLanguage.English, descriptor.Language, "Polish base game language");
        Equal("pl", descriptor.Code, "Polish code");
        Equal("Polish", descriptor.DisplayName, "Polish display name");
        Equal("lang-en", descriptor.LanguageDirectoryName, "Polish language directory");
        Equal("flevel.lgp", descriptor.FieldArchiveName, "Polish field archive");
        Equal(Ff7TextEncodingProfile.PolishFanTranslation, descriptor.TextEncodingProfile, "Polish text encoding");
        Equal(false, descriptor.UsesJapaneseEncoding, "Polish Japanese encoding flag");
        Equal(5, Ff7GameLanguages.All.Count, "official language enumeration excludes fan profiles");
    }

    private static void RecognizesKnownPolishTranslationFingerprint()
    {
        Equal(
            true,
            Ff7GameLanguageDetector.TryMatchFanTranslationFingerprint(
                13_170,
                "84886B3F59DFB302A2936B3924E8C468790D582C3F11FC0508106DA42A01FEA3",
                out var descriptor),
            "Polish WINDOW.BIN fingerprint");
        Equal("pl", descriptor.Code, "fingerprinted Polish profile");
        Equal(
            false,
            Ff7GameLanguageDetector.TryMatchFanTranslationFingerprint(
                13_266,
                "E4D135CE630E59D0DF17A23C7BF1BCA2B590464D4F8F4E3D42E8C0E5A448C2A8",
                out _),
            "vanilla English WINDOW.BIN fingerprint");
    }

    private static void RecognizesPolishKernelTextWhenTheTranslationFontIsUnavailable()
    {
        using var game = TestGame.Create("en");
        game.WriteKernel2(BuildPolishKernel2());
        var manifest = Path.Combine(game.Root, "appmanifest_3837340.acf");
        File.WriteAllText(
            manifest,
            "\"AppState\" { \"installdir\" \"" + Path.GetFileName(game.Root) +
            "\" \"UserConfig\" { \"language\" \"english\" } }");

        var context = Ff7GameLanguageDetector.Detect(
            game.Root,
            "auto",
            Path.Combine(game.Root, "ff7_en.exe"),
            new[] { manifest });

        Equal("pl", context.Code, "Polish KERNEL2 semantic fingerprint");
        Equal(
            Ff7GameLanguageDetectionSource.TranslationFingerprint,
            context.Source,
            "Polish KERNEL2 detection source");
        Equal(
            "installed Polish translation data",
            context.DetectionDetail,
            "Polish KERNEL2 detection detail");
    }

    private static byte[] BuildPolishKernel2()
    {
        int[] sectionCounts =
        [
            32, 256, 128, 128, 32, 32, 96, 64, 32,
            256, 128, 128, 32, 32, 96, 64, 128, 16
        ];
        using var decoded = new MemoryStream();
        for (var section = 0; section < sectionCounts.Length; section++)
        {
            var strings = Enumerable.Range(0, sectionCounts[section])
                .Select(index => section == 2 ? index switch
                {
                    0 => EncodePolish("Przywraca 100 HP"),
                    2 => EncodePolish("W pełni odnawia HP"),
                    20 => EncodePolish("Eksplozja rażąca przeciwnika"),
                    100 => EncodePolish("Autograf Mistrza Dio"),
                    _ => EncodePolish($"Opis {index}")
                } : EncodePolish($"S{section:D2}I{index:D3}"))
                .ToArray();
            var tableSize = strings.Length * sizeof(ushort);
            var sectionSize = tableSize + strings.Sum(value => value.Length);
            decoded.Write(BitConverter.GetBytes(sectionSize));
            var relativeOffset = tableSize;
            foreach (var value in strings)
            {
                decoded.Write(BitConverter.GetBytes((ushort)relativeOffset));
                relativeOffset += value.Length;
            }

            foreach (var value in strings)
            {
                decoded.Write(value);
            }
        }

        var uncompressed = decoded.ToArray();
        using var lzs = new MemoryStream();
        for (var offset = 0; offset < uncompressed.Length; offset += 8)
        {
            var count = Math.Min(8, uncompressed.Length - offset);
            lzs.WriteByte((byte)((1 << count) - 1));
            lzs.Write(uncompressed, offset, count);
        }

        return lzs.ToArray();
    }

    private static byte[] EncodePolish(string value)
    {
        var bytes = new List<byte>(value.Length + 1);
        foreach (var character in value)
        {
            bytes.Add(character switch
            {
                'ą' => 0x67,
                'ć' => 0x74,
                'ł' => 0x75,
                'ń' => 0x76,
                'ę' => 0x78,
                'Ł' => 0x79,
                'Ś' => 0x7a,
                'Ć' => 0x7b,
                'ź' => 0x7c,
                'ż' => 0x8d,
                'Ż' => 0x91,
                'ś' => 0xa0,
                >= ' ' and <= '~' => (byte)(character - 0x20),
                _ => throw new InvalidOperationException($"Unsupported Polish fixture character: {character}")
            });
        }

        bytes.Add(0xff);
        return bytes.ToArray();
    }

    private static void MapsEverySupportedLanguageToItsNativeAssets()
    {
        var expected = new[]
        {
            (Ff7GameLanguage.English, "en", "lang-en", "flevel.lgp", false),
            (Ff7GameLanguage.French, "fr", "lang-fr", "fflevel.lgp", false),
            (Ff7GameLanguage.German, "de", "lang-de", "gflevel.lgp", false),
            (Ff7GameLanguage.Spanish, "es", "lang-es", "sflevel.lgp", false),
            (Ff7GameLanguage.Japanese, "ja", "lang-ja", "jfleve.lgp", true)
        };

        foreach (var row in expected)
        {
            var descriptor = Ff7GameLanguages.Get(row.Item1);
            Equal(row.Item2, descriptor.Code, $"{row.Item1} code");
            Equal(row.Item3, descriptor.LanguageDirectoryName, $"{row.Item1} directory");
            Equal(row.Item4, descriptor.FieldArchiveName, $"{row.Item1} field archive");
            Equal(row.Item5, descriptor.UsesJapaneseEncoding, $"{row.Item1} encoding");
        }
    }

    private static void ExplicitOverrideWinsWhenItsDataExists()
    {
        using var game = TestGame.Create("en", "fr");
        var context = Ff7GameLanguageDetector.Detect(
            game.Root,
            "fr",
            Path.Combine(game.Root, "ff7_en.exe"),
            Array.Empty<string>());

        Equal(Ff7GameLanguage.French, context.Language, "explicit override");
        Equal(Ff7GameLanguageDetectionSource.Configuration, context.Source, "override source");
    }

    private static void ExplicitPolishOverrideUsesEnglishAssetPaths()
    {
        using var game = TestGame.Create("en");
        var context = Ff7GameLanguageDetector.Detect(
            game.Root,
            "pl",
            Path.Combine(game.Root, "ff7_en.exe"),
            Array.Empty<string>());

        Equal(Ff7GameLanguage.English, context.Language, "Polish base language override");
        Equal("pl", context.Code, "Polish override code");
        Equal(Ff7TextEncodingProfile.PolishFanTranslation, context.Descriptor.TextEncodingProfile, "Polish override encoding");
        Equal(Ff7GameLanguageDetectionSource.Configuration, context.Source, "Polish override source");
    }

    private static void ExecutableSuffixWinsDuringAutomaticDetection()
    {
        using var game = TestGame.Create("en", "de");
        var context = Ff7GameLanguageDetector.Detect(
            game.Root,
            "auto",
            Path.Combine(game.Root, "ff7_de.exe"),
            Array.Empty<string>());

        Equal(Ff7GameLanguage.German, context.Language, "executable suffix");
        Equal(Ff7GameLanguageDetectionSource.Executable, context.Source, "executable source");
    }

    private static void MatchingSteamManifestSuppliesTheLanguage()
    {
        using var game = TestGame.Create("en", "es");
        var manifest = Path.Combine(game.Root, "appmanifest_39140.acf");
        File.WriteAllText(
            manifest,
            "\"AppState\"\n{\n  \"installdir\" \"" + Path.GetFileName(game.Root) +
            "\"\n  \"UserConfig\"\n  {\n    \"language\" \"spanish\"\n  }\n}\n");

        var context = Ff7GameLanguageDetector.Detect(
            game.Root,
            "auto",
            Path.Combine(game.Root, "ff7.exe"),
            new[] { manifest });

        Equal(Ff7GameLanguage.Spanish, context.Language, "Steam language");
        Equal(Ff7GameLanguageDetectionSource.SteamManifest, context.Source, "Steam source");
    }

    private static void AStaleOverrideFallsThroughInsteadOfSelectingMissingData()
    {
        using var game = TestGame.Create("en", "ja");
        var context = Ff7GameLanguageDetector.Detect(
            game.Root,
            "fr",
            Path.Combine(game.Root, "ff7_ja.exe"),
            Array.Empty<string>());

        Equal(Ff7GameLanguage.Japanese, context.Language, "stale override fallback");
        Equal(Ff7GameLanguageDetectionSource.Executable, context.Source, "fallback source");
    }

    private static void AStaleManifestFallsThroughToTheOnlyUsableLanguage()
    {
        using var game = TestGame.Create("de");
        var manifest = Path.Combine(game.Root, "appmanifest_3837340.acf");
        File.WriteAllText(
            manifest,
            "\"AppState\" { \"installdir\" \"" + Path.GetFileName(game.Root) +
            "\" \"UserConfig\" { \"language\" \"japanese\" } }");

        var context = Ff7GameLanguageDetector.Detect(
            game.Root,
            "auto",
            Path.Combine(game.Root, "ff7.exe"),
            new[] { manifest });

        Equal(Ff7GameLanguage.German, context.Language, "single installed language fallback");
        Equal(Ff7GameLanguageDetectionSource.SingleInstalledLanguage, context.Source, "single language source");
    }

    private static void InvalidOverrideFallsThroughToAutomaticDetection()
    {
        using var game = TestGame.Create("en", "fr");
        var messages = new List<string>();
        var context = Ff7GameLanguageDetector.Detect(
            game.Root,
            "klingon",
            Path.Combine(game.Root, "ff7_fr.exe"),
            Array.Empty<string>(),
            messages.Add);

        Equal(Ff7GameLanguage.French, context.Language, "invalid override fallback");
        True(messages.Any(message => message.Contains("klingon", StringComparison.OrdinalIgnoreCase)), "invalid override diagnostic");
    }

    private static void EnglishIsTheFinalFallback()
    {
        using var game = TestGame.CreateWithoutLanguages();
        var context = Ff7GameLanguageDetector.Detect(
            game.Root,
            "auto",
            Path.Combine(game.Root, "ff7.exe"),
            Array.Empty<string>());

        Equal(Ff7GameLanguage.English, context.Language, "final English fallback");
        Equal(Ff7GameLanguageDetectionSource.EnglishFallback, context.Source, "English fallback source");
        True(context.Kernel2Path.EndsWith(Path.Combine("lang-en", "kernel", "kernel2.bin"), StringComparison.OrdinalIgnoreCase), "fallback kernel path");
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }

    private static void True(bool condition, string label)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"{label}: expected true");
        }
    }

    private sealed class TestGame : IDisposable
    {
        private TestGame(string root)
        {
            Root = root;
        }

        public string Root { get; }

        public static TestGame Create(params string[] languageCodes)
        {
            var game = CreateWithoutLanguages();
            foreach (var code in languageCodes)
            {
                var kernelDirectory = Path.Combine(game.Root, "data", $"lang-{code}", "kernel");
                Directory.CreateDirectory(kernelDirectory);
                File.WriteAllBytes(Path.Combine(kernelDirectory, "kernel2.bin"), new byte[] { 1 });
            }

            return game;
        }

        public static TestGame CreateWithoutLanguages()
        {
            var root = Path.Combine(Path.GetTempPath(), "blind-soldier-language-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "data", "field"));
            return new TestGame(root);
        }

        public void WriteKernel2(byte[] bytes)
        {
            File.WriteAllBytes(
                Path.Combine(Root, "data", "lang-en", "kernel", "kernel2.bin"),
                bytes);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
