using System.Text.Encodings.Web;
using System.Text.Json;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class AudioDescriptionLocalizationTests
{
    // Reviewed identical wording is legitimate, but its explicit catalog entry
    // is still required: an absent entry must never pass as an English fallback.
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> ReviewedIdenticalTranslations =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["de"] = new HashSet<string>(StringComparer.Ordinal) { "Barret: Hey!" }
        };

    internal static void Run()
    {
        var texts = EnglishTexts();
        foreach (var language in Ff7GameLanguages.All.Where(language => language.Code != "en")
                     .Append(Ff7GameLanguages.PolishFanTranslation).Append(Ff7GameLanguages.RussianFanTranslation))
        {
            var localizer = BlindSoldierLocalizer.Create(language, null);
            var assembly = typeof(BlindSoldierLocalizer).Assembly;
            var resource = assembly.GetManifestResourceNames().Single(name =>
                name.EndsWith($".AudioDescriptionLocalization.{language.Code}.json", StringComparison.Ordinal));
            using var catalogStream = assembly.GetManifestResourceStream(resource)!;
            using var catalog = JsonDocument.Parse(catalogStream);
            if (localizer.UseRecordedAudioDescriptions ||
                OpeningMovieAudioTrackPolicy.ShouldUseReloadedPlayback(true, true, localizer.UseRecordedAudioDescriptions) ||
                !OpeningMovieAudioTrackPolicy.ShouldUseScreenReader(true, false, localizer.UseRecordedAudioDescriptions))
                throw new InvalidOperationException($"{language.Code} must use screen-reader descriptions, including the default opening option.");
            foreach (var text in texts)
            {
                var translated = localizer.LocalizeDescription(text);
                var reviewedIdentical = ReviewedIdenticalTranslations.TryGetValue(language.Code, out var identical) && identical.Contains(text);
                if (!catalog.RootElement.TryGetProperty(text, out var entry) || entry.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(translated) || (translated == text && !reviewedIdentical))
                    throw new InvalidOperationException($"Missing {language.Code} description translation: {text}");
                if (System.Text.RegularExpressions.Regex.IsMatch(translated, "ZXQ|▁|@@|�",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    throw new InvalidOperationException($"Unrestored translation marker in {language.Code}: {text}");
                foreach (var sentence in System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?])\s+"))
                    if (sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 5 &&
                        translated.Contains(sentence, StringComparison.Ordinal))
                        throw new InvalidOperationException($"Untranslated sentence in {language.Code}: {sentence}");
            }
            if (localizer.Localize("Native dialogue not in the description catalog.") !=
                "Native dialogue not in the description catalog.")
                throw new InvalidOperationException("Description localization changed native dialogue.");
            const string sharedNativeLine = "Cloud: Lifestream.";
            if (localizer.Localize(sharedNativeLine) != sharedNativeLine)
                throw new InvalidOperationException("Native dialogue acquired a description translation.");
            var room = FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions().First();
            var status = FieldAreaDescriptionStatus.Append(localizer.Localize("Route complete."), room.FieldId,
                localizer.LocalizeDescription);
            if (status.Contains(room.Text, StringComparison.Ordinal) ||
                !status.Contains(localizer.LocalizeDescription(room.Text), StringComparison.Ordinal))
                throw new InvalidOperationException("On-demand area status lost its translated description.");
        }
        var english = BlindSoldierLocalizer.Create(Ff7GameLanguages.Get(Ff7GameLanguage.English), null);
        if (!english.UseRecordedAudioDescriptions ||
            OpeningMovieAudioTrackPolicy.ShouldUseScreenReader(true, false, english.UseRecordedAudioDescriptions) ||
            OpeningMovieAudioTrackPolicy.ShouldUseScreenReader(false, false, false))
            throw new InvalidOperationException("Description language policy changed English defaults or ignored disabled options.");
        if (texts.Any(text => english.LocalizeDescription(text) != text))
            throw new InvalidOperationException("English descriptions changed.");
        Console.WriteLine($"Description translations cover {texts.Count} distinct texts in six non-English languages.");
    }

    internal static void Export(string path) => File.WriteAllText(path,
        JsonSerializer.Serialize(EnglishTexts(), new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }));

    private static IReadOnlyList<string> EnglishTexts()
    {
        var source = Environment.GetEnvironmentVariable("FF7_ACCESSIBILITY_SOURCE_ROOT")
            ?? FindSourceRoot();
        var assets = Path.Combine(source, "Ff7.Accessibility.Reloaded", "Assets");
        var texts = FieldCutsceneDescriptionCatalog.CreateEarlyGameDescriptions()
            .Concat(FieldCutsceneDescriptionCatalog.CreateAllAreaDescriptions())
            .Select(cue => cue.Text)
            .Concat(OpeningMovieDescription.Cues.Select(cue => cue.Text))
            .Concat(MovieFilmDescriptionCatalog.Paragraphs.Values)
            .Append(NativeCreditsNarrator.Introduction).ToHashSet(StringComparer.Ordinal);
        using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "cutscene-voice", "manifest.json"))))
            foreach (var entry in manifest.RootElement.GetProperty("entries").EnumerateArray())
                texts.Add(entry.GetProperty("text").GetString()!);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(assets, "movies"), "*.json"))
            foreach (var cue in MovieNarrationCueSchedule.Parse(File.ReadAllText(path)))
                texts.Add(cue.Text);
        using (var battle = JsonDocument.Parse(File.ReadAllText(Path.Combine(assets, "cutscene-voice", "battle-animations.json"))))
            foreach (var animation in battle.RootElement.EnumerateArray())
                foreach (var cue in animation.GetProperty("cues").EnumerateArray())
                    texts.Add(cue.GetProperty("text").GetString()!);
        return texts.Order(StringComparer.Ordinal).ToArray();
    }

    private static string FindSourceRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Ff7.Accessibility.Reloaded", "Ff7.Accessibility.Reloaded.csproj")))
                    return directory.FullName;
        throw new InvalidOperationException("Set FF7_ACCESSIBILITY_SOURCE_ROOT for description coverage.");
    }
}
