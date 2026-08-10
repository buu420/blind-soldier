namespace Ff7.Accessibility.Reloaded;

public enum Ff7GameLanguage
{
    English,
    French,
    German,
    Spanish,
    Japanese
}

public enum Ff7TextEncodingProfile
{
    Western,
    Japanese,
    PolishFanTranslation
}

public enum Ff7GameLanguageDetectionSource
{
    Configuration,
    TranslationFingerprint,
    Executable,
    SteamManifest,
    SingleInstalledLanguage,
    EnglishFallback
}

public sealed record Ff7GameLanguageDescriptor(
    Ff7GameLanguage Language,
    string Code,
    string DisplayName,
    string SteamName,
    string LanguageDirectoryName,
    string FieldArchiveName,
    Ff7TextEncodingProfile TextEncodingProfile)
{
    public bool UsesJapaneseEncoding => TextEncodingProfile == Ff7TextEncodingProfile.Japanese;
}

public sealed record Ff7GameLanguageContext(
    Ff7GameLanguageDescriptor Descriptor,
    string DataDirectory,
    Ff7GameLanguageDetectionSource Source,
    string DetectionDetail)
{
    public Ff7GameLanguage Language => Descriptor.Language;

    public string Code => Descriptor.Code;

    public string DisplayName => Descriptor.DisplayName;

    public bool UsesJapaneseEncoding => Descriptor.UsesJapaneseEncoding;

    public Ff7TextEncodingProfile TextEncodingProfile => Descriptor.TextEncodingProfile;

    public string LanguageDirectory => Path.Combine(DataDirectory, Descriptor.LanguageDirectoryName);

    public string Kernel2Path => Path.Combine(LanguageDirectory, "kernel", "kernel2.bin");

    public string FieldArchivePath => Path.Combine(DataDirectory, "field", Descriptor.FieldArchiveName);
}

public static class Ff7GameLanguages
{
    private static readonly Ff7GameLanguageDescriptor[] Descriptors =
    [
        new(Ff7GameLanguage.English, "en", "English", "english", "lang-en", "flevel.lgp", Ff7TextEncodingProfile.Western),
        new(Ff7GameLanguage.French, "fr", "French", "french", "lang-fr", "fflevel.lgp", Ff7TextEncodingProfile.Western),
        new(Ff7GameLanguage.German, "de", "German", "german", "lang-de", "gflevel.lgp", Ff7TextEncodingProfile.Western),
        new(Ff7GameLanguage.Spanish, "es", "Spanish", "spanish", "lang-es", "sflevel.lgp", Ff7TextEncodingProfile.Western),
        new(Ff7GameLanguage.Japanese, "ja", "Japanese", "japanese", "lang-ja", "jfleve.lgp", Ff7TextEncodingProfile.Japanese)
    ];

    private static readonly Ff7GameLanguageDescriptor PolishFanTranslationDescriptor =
        new(
            Ff7GameLanguage.English,
            "pl",
            "Polish",
            "polish",
            "lang-en",
            "flevel.lgp",
            Ff7TextEncodingProfile.PolishFanTranslation);

    private static readonly Ff7GameLanguageDescriptor[] ParseableDescriptors =
        [.. Descriptors, PolishFanTranslationDescriptor];

    public static IReadOnlyList<Ff7GameLanguageDescriptor> All => Descriptors;

    public static Ff7GameLanguageDescriptor PolishFanTranslation => PolishFanTranslationDescriptor;

    public static Ff7GameLanguageDescriptor Get(Ff7GameLanguage language) =>
        Descriptors.Single(descriptor => descriptor.Language == language);

    public static bool TryParse(string? value, out Ff7GameLanguageDescriptor descriptor)
    {
        var normalized = value?.Trim();
        descriptor = Descriptors[0];
        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        var match = ParseableDescriptors.FirstOrDefault(candidate =>
            string.Equals(candidate.Code, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.DisplayName, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.SteamName, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.LanguageDirectoryName, normalized, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        descriptor = match;
        return true;
    }
}
