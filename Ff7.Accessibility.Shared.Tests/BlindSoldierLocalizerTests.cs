using Ff7.Accessibility.Reloaded;

internal static class BlindSoldierLocalizerTests
{
    public static void Run()
    {
        LocalizesExactCoreSpeechInEverySupportedLanguage();
        LocalizesTemplatesWithoutChangingCapturedNativeNames();
        LocalizesGeneratedMenuSummaries();
        LeavesUnknownNativeTextUntouched();
        FallsBackToEnglishForAMissingKnownTranslation();
        LoadsAValidBoundedExternalOverride();
        RejectsInvalidAndOversizedOverrides();
    }

    private static void LocalizesExactCoreSpeechInEverySupportedLanguage()
    {
        Equal("Route complete.", Create(Ff7GameLanguage.English).Localize("Route complete."), "English exact");
        Equal("Itinéraire terminé.", Create(Ff7GameLanguage.French).Localize("Route complete."), "French exact");
        Equal("Route abgeschlossen.", Create(Ff7GameLanguage.German).Localize("Route complete."), "German exact");
        Equal("Ruta completada.", Create(Ff7GameLanguage.Spanish).Localize("Route complete."), "Spanish exact");
        Equal("ルートが完了しました。", Create(Ff7GameLanguage.Japanese).Localize("Route complete."), "Japanese exact");
    }

    private static void LocalizesTemplatesWithoutChangingCapturedNativeNames()
    {
        Equal(
            "Cloud. PV 100 sur 200. PM 10 sur 20.",
            Create(Ff7GameLanguage.French).Localize("Cloud. HP 100 of 200. MP 10 of 20."),
            "French battle template");
        Equal(
            "Barret, LP 321 von 400.",
            Create(Ff7GameLanguage.German).Localize("Barret, HP 321 of 400."),
            "German HP template");
        Equal(
            "Aerisに到着しました。ナビゲーションを終了します。",
            Create(Ff7GameLanguage.Japanese).Localize("Arrived at Aeris. Navigation off."),
            "Japanese arrival template");
        Equal(
            "arriba 20",
            Create(Ff7GameLanguage.Spanish).Localize("up 20"),
            "Spanish direction template");
        Equal(
            "Histoire, Aeris. Itinéraire indisponible.",
            Create(Ff7GameLanguage.French).Localize("Story, Aeris. Route unavailable."),
            "known generated template captures are localized while native names remain unchanged");
    }

    private static void LeavesUnknownNativeTextUntouched()
    {
        const string nativeText = "Épée broyante";
        Equal(nativeText, Create(Ff7GameLanguage.French).Localize(nativeText), "native text pass-through");
    }

    private static void LocalizesGeneratedMenuSummaries()
    {
        var japanese = Create(Ff7GameLanguage.Japanese);
        Equal("ニューゲーム", japanese.Localize("New Game"), "Japanese generated title label");
        Equal(
            "編成。パーティ枠 1、クラウド。STARTボタンで決定",
            japanese.Localize("編成. Party slot 1, クラウド. STARTボタンで決定"),
            "Japanese party formation summary");

        var french = Create(Ff7GameLanguage.French);
        Equal(
            "Vitesse des messages. 50 pour cent de rapide à lent. Régler la vitesse",
            french.Localize("Vitesse des messages. 50 percent from Fast to Slow. Régler la vitesse"),
            "French generated Config slider value");
        Equal(
            "Sélectionnez un fichier de sauvegarde. Sauvegarde 2, vide.",
            french.Localize("Select a save data file. Save 2, empty."),
            "French generated save-file summary");
    }

    private static void FallsBackToEnglishForAMissingKnownTranslation()
    {
        var english = new Dictionary<string, string> { ["Known {0}"] = "Known {0}" };
        var localizer = BlindSoldierLocalizer.CreateForTesting(
            Ff7GameLanguages.Get(Ff7GameLanguage.French),
            english,
            new Dictionary<string, string>());

        Equal("Known value", localizer.Localize("Known value"), "per-key English fallback");
    }

    private static void LoadsAValidBoundedExternalOverride()
    {
        using var directory = TemporaryDirectory.Create();
        var languageDirectory = Path.Combine(directory.Path, "Languages");
        Directory.CreateDirectory(languageDirectory);
        File.WriteAllText(
            Path.Combine(languageDirectory, "fr.json"),
            "{\"Route complete.\":\"Trajet terminé.\"}");

        var localizer = BlindSoldierLocalizer.Create(
            Ff7GameLanguages.Get(Ff7GameLanguage.French),
            directory.Path);
        Equal("Trajet terminé.", localizer.Localize("Route complete."), "external override");
    }

    private static void RejectsInvalidAndOversizedOverrides()
    {
        using var invalidDirectory = TemporaryDirectory.Create();
        var invalidLanguages = Path.Combine(invalidDirectory.Path, "Languages");
        Directory.CreateDirectory(invalidLanguages);
        File.WriteAllText(Path.Combine(invalidLanguages, "fr.json"), "[not an object]");
        var messages = new List<string>();
        var invalid = BlindSoldierLocalizer.Create(
            Ff7GameLanguages.Get(Ff7GameLanguage.French),
            invalidDirectory.Path,
            messages.Add);
        Equal("Itinéraire terminé.", invalid.Localize("Route complete."), "invalid override fallback");
        True(messages.Any(message => message.Contains("override", StringComparison.OrdinalIgnoreCase)), "invalid override diagnostic");

        using var largeDirectory = TemporaryDirectory.Create();
        var largeLanguages = Path.Combine(largeDirectory.Path, "Languages");
        Directory.CreateDirectory(largeLanguages);
        File.WriteAllText(Path.Combine(largeLanguages, "fr.json"), new string(' ', BlindSoldierLocalizer.MaximumOverrideBytes + 1));
        messages.Clear();
        var large = BlindSoldierLocalizer.Create(
            Ff7GameLanguages.Get(Ff7GameLanguage.French),
            largeDirectory.Path,
            messages.Add);
        Equal("Itinéraire terminé.", large.Localize("Route complete."), "large override fallback");
        True(messages.Any(message => message.Contains("too large", StringComparison.OrdinalIgnoreCase)), "large override diagnostic");
    }

    private static BlindSoldierLocalizer Create(Ff7GameLanguage language) =>
        BlindSoldierLocalizer.Create(Ff7GameLanguages.Get(language), modDirectory: null);

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
        }
    }

    private static void True(bool value, string label)
    {
        if (!value)
        {
            throw new InvalidOperationException($"{label}: expected true");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "blind-soldier-localizer-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
