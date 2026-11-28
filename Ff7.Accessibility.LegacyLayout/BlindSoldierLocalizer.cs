using System.Reflection;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ff7.Accessibility.Reloaded;

public sealed partial class BlindSoldierLocalizer
{
    public const int MaximumOverrideBytes = 256 * 1024;
    private const int MaximumOverrideEntries = 2048;
    private const int MaximumEntryLength = 2048;

    private readonly Ff7GameLanguageDescriptor language;
    private readonly IReadOnlyDictionary<string, string> english;
    private readonly IReadOnlyDictionary<string, string> localized;
    private readonly IReadOnlyDictionary<string, string> descriptionSources;
    private readonly IReadOnlyDictionary<string, string> descriptions;
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> DescriptionCatalogs = new(StringComparer.Ordinal);
    // Known wrappers added to native ASK rows by the mod. Their contents stay
    // native text; availability and cursor position are generated metadata.
    private static readonly IReadOnlySet<string> NativeMetadataTemplates = new HashSet<string>(StringComparer.Ordinal)
    {
        "{0}, choice {1} of {2}", "{0} unavailable, choice {1} of {2}", "{0}, {1} available", "{0} unavailable"
    };
    // Languages whose own-speech catalogs include the generated runtime prompts and trusted
    // composition entries: composed mod speech is localized by parts, and only base catalog
    // templates ever match native text.
    private static readonly IReadOnlySet<string> ComposingLanguages = new HashSet<string>(StringComparer.Ordinal) { "ru", "pl" };
    private readonly IReadOnlyList<TemplateTranslation> templates;
    private readonly IReadOnlyList<TemplateTranslation> modTemplates;
    private readonly Action<string>? log;
    private readonly HashSet<string> loggedFallbacks = new(StringComparer.Ordinal);

    private BlindSoldierLocalizer(
        Ff7GameLanguageDescriptor language,
        IReadOnlyDictionary<string, string> english,
        IReadOnlyDictionary<string, string> localized,
        Action<string>? log,
        IReadOnlyDictionary<string, string>? descriptionSources = null,
        IReadOnlyDictionary<string, string>? descriptions = null,
        IReadOnlySet<string>? modOnlyKeys = null)
    {
        this.language = language;
        this.english = english;
        this.localized = localized;
        this.descriptionSources = descriptionSources ?? new Dictionary<string, string>(StringComparer.Ordinal);
        this.descriptions = descriptions ?? new Dictionary<string, string>(StringComparer.Ordinal);
        this.log = log;
        modTemplates = english.Keys
            .Concat(localized.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(source => PlaceholderPattern().IsMatch(source))
            .Select(source => TemplateTranslation.Create(source, guardNativeAbbreviations: language.Code == "ru"))
            .OrderByDescending(template => template.LiteralLength)
            .ToArray();
        // Short structural formats are useful for trusted, generated UI ("1 of
        // 80.") but must not become patterns over arbitrary native dialogue.
        templates = ComposingLanguages.Contains(language.Code)
            ? modTemplates.Where(template => modOnlyKeys?.Contains(template.Source) != true &&
                (english.ContainsKey(template.Source) || NativeMetadataTemplates.Contains(template.Source))).ToArray()
            : modTemplates;
    }

    public string LanguageCode => language.Code;

    /// <summary>Whether mod-generated speech is localized by parts (<see cref="LocalizeModSpeech"/>).</summary>
    public bool ComposesModSpeech => ComposingLanguages.Contains(language.Code);

    // Polish uses the English game enum and a separate text encoding. The code,
    // not that enum, determines whether the packaged English recordings fit.
    public bool UseRecordedAudioDescriptions =>
        string.Equals(language.Code, "en", StringComparison.OrdinalIgnoreCase);

    public static BlindSoldierLocalizer Create(
        Ff7GameLanguageDescriptor language,
        string? modDirectory,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(language);
        var english = LoadEmbedded("en");
        var localized = string.Equals(language.Code, "en", StringComparison.OrdinalIgnoreCase)
            ? new Dictionary<string, string>(english, StringComparer.Ordinal)
            : LoadEmbedded(language.Code);
        var composing = ComposingLanguages.Contains(language.Code);
        if (composing)
            foreach (var pair in LoadResourceCatalog($".Localization.{language.Code}.runtime.json"))
                localized.TryAdd(pair.Key, pair.Value);
        var composition = composing
            ? LoadResourceCatalog($".Localization.{language.Code}.composition.json") : new Dictionary<string, string>();
        foreach (var pair in composition) localized.TryAdd(pair.Key, pair.Value);
        var useDescriptions = !string.Equals(language.Code, "en", StringComparison.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, string> descriptionSources = useDescriptions
            ? LoadEmbeddedDescriptions("en") : new Dictionary<string, string>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, string> descriptions = useDescriptions
            ? LoadEmbeddedDescriptions(language.Code) : new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string>? descriptionOverrides = null;

        if (!string.IsNullOrWhiteSpace(modDirectory))
        {
            var overridePath = Path.Combine(modDirectory, "Languages", $"{language.Code}.json");
            foreach (var pair in LoadOverride(overridePath, log))
            {
                if (descriptionSources.ContainsKey(pair.Key))
                {
                    descriptionOverrides ??= new Dictionary<string, string>(descriptions, StringComparer.Ordinal);
                    descriptionOverrides[pair.Key] = pair.Value;
                }
                else localized[pair.Key] = pair.Value;
            }
        }

        return new BlindSoldierLocalizer(language, english, localized, log,
            descriptionSources, descriptionOverrides ?? descriptions, composition.Keys.ToHashSet(StringComparer.Ordinal));
    }

    internal static BlindSoldierLocalizer CreateForTesting(
        Ff7GameLanguageDescriptor language,
        IReadOnlyDictionary<string, string> english,
        IReadOnlyDictionary<string, string> localized,
        Action<string>? log = null) =>
        new(language, english, localized, log);

    public string Localize(string text) => LocalizeCore(text, templates,
        ComposesModSpeech ? value => LocalizeCore(value, templates, LocalizeCapturedValue) : LocalizeCapturedValue);

    private string LocalizeCore(string text, IReadOnlyList<TemplateTranslation> candidates, Func<string, string> localizeCapture,
        Func<TemplateTranslation, Match, bool>? accepts = null)
    {
        if (string.IsNullOrWhiteSpace(text) ||
            string.Equals(language.Code, "en", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        if (localized.TryGetValue(text, out var exactTranslation))
        {
            return exactTranslation;
        }

        if (english.ContainsKey(text))
        {
            LogEnglishFallback(text);
            return english[text];
        }

        foreach (var template in candidates)
        {
            Match match;
            try { match = template.Pattern.Match(text); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!match.Success || accepts?.Invoke(template, match) == false)
            {
                continue;
            }

            string target;
            if (!localized.TryGetValue(template.Source, out target!))
            {
                if (!english.TryGetValue(template.Source, out target!))
                {
                    continue;
                }

                LogEnglishFallback(template.Source);
            }

            return template.Apply(target, match, localizeCapture, language.Code);
        }

        return text;
    }

    /// <summary>
    /// Translates composed mod-generated settings/navigation speech. Callers
    /// must not send native dialogue, custom names or production credits here.
    /// Other language pipelines retain their existing behavior.
    /// </summary>
    public string LocalizeModSpeech(string text) => ComposesModSpeech
        ? LocalizeModParts(text, 0) : Localize(text);

    private string LocalizeModParts(string text, int depth)
    {
        if (depth >= 10 || string.IsNullOrWhiteSpace(text)) return text;
        var translated = LocalizeCore(text, modTemplates, value => LocalizeModParts(value, depth + 1), AcceptsModMatch);
        if (translated != text) return translated;
        // Sentences first, so a sentence template ("Enemies: {0}.") is still tried whole
        // inside a longer line; clauses only within a sentence that has no entry.
        foreach (var separators in new[] { ModSentenceBreak(), ModClauseBreak() })
        {
            var matches = separators.Matches(text);
            if (matches.Count == 0) continue;
            var result = new System.Text.StringBuilder();
            var cursor = 0;
            foreach (Match separator in matches)
            {
                var punctuation = separator.Value[0] is '.' or '!' or '?' ? 1 : 0;
                result.Append(LocalizeModParts(text[cursor..(separator.Index + punctuation)], depth + 1));
                result.Append(separator.Value[punctuation..]);
                cursor = separator.Index + separator.Length;
            }
            result.Append(LocalizeModParts(text[cursor..], depth + 1));
            return result.ToString();
        }
        if (text.Length > 1 && text[^1] is '.' or '!' or '?')
            return LocalizeModParts(text[..^1], depth + 1) + text[^1];
        return text;
    }

    // A capture enclosed by template text never runs across a sentence end ("Left {0}." must
    // not swallow the next sentence; "Navigation menu. {0}" may still take the rest of the
    // line). A frame with no word of its own ("{0} of {1}.") only counts beside a number, so
    // "Let go of the keys" and "Temple of the Ancients" are not rewritten.
    private static bool AcceptsModMatch(TemplateTranslation template, Match match)
    {
        var captures = template.GroupNames.Values.Select(name => match.Groups[name]).ToArray();
        if (captures.Any(group => group.Index > 0 && group.Index + group.Length < match.Length &&
                ModSentenceBreak().IsMatch(group.Value)))
            return false;
        // A one-sentence frame ("{0}, {1} and {2}") never takes whole sentences at its edges
        // either: "Mod settings. J and L move…, U and O change…" is three sentences, not a list.
        if (!template.SpansSentences && captures.Any(group => ModSentenceBreak().IsMatch(group.Value)))
            return false;
        return GeneralTemplateWord().IsMatch(PlaceholderPattern().Replace(template.Source, "")) ||
            captures.Any(group => NumberAtEdge().IsMatch(group.Value));
    }

    // Only description delivery calls this. Movie sidecars can contain a line
    // that is also native dialogue; merely loading their translations must not
    // rewrite that dialogue or displayed production-credit rows.
    public string LocalizeDescription(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || UseRecordedAudioDescriptions) return text;
        if (localized.TryGetValue(text, out var overridden)) return overridden;
        if (descriptions.TryGetValue(text, out var translated)) return translated;
        if (descriptionSources.ContainsKey(text)) LogEnglishFallback(text);
        return Localize(text);
    }

    private string LocalizeCapturedValue(string value) =>
        localized.TryGetValue(value, out var translation) ? translation : value;

    private void LogEnglishFallback(string key)
    {
        if (loggedFallbacks.Add(key))
        {
            log?.Invoke($"Blind Soldier {language.Code} translation missing for '{key}'; using English fallback.");
        }
    }

    private static Dictionary<string, string> LoadEmbedded(string code)
        => LoadResourceCatalog($".Localization.{code}.json");

    private static IReadOnlyDictionary<string, string> LoadEmbeddedDescriptions(string code)
        => DescriptionCatalogs.GetOrAdd(code, static key => LoadResourceCatalog($".AudioDescriptionLocalization.{key}.json"));

    private static Dictionary<string, string> LoadResourceCatalog(string suffix)
    {
        var assembly = typeof(BlindSoldierLocalizer).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Embedded localization resource could not be opened: {resourceName}");
        return ReadCatalog(stream);
    }

    private static Dictionary<string, string> LoadOverride(string path, Action<string>? log)
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
        {
            return empty;
        }

        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaximumOverrideBytes)
            {
                log?.Invoke($"Blind Soldier language override is too large and was ignored: {path}");
                return empty;
            }

            using var stream = File.OpenRead(path);
            var catalog = ReadCatalog(stream);
            if (catalog.Count > MaximumOverrideEntries ||
                catalog.Any(pair => pair.Key.Length > MaximumEntryLength || pair.Value.Length > MaximumEntryLength))
            {
                log?.Invoke($"Blind Soldier language override exceeds entry limits and was ignored: {path}");
                return empty;
            }

            log?.Invoke($"Blind Soldier language override loaded: {path}; entries={catalog.Count}");
            return catalog;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            log?.Invoke($"Blind Soldier language override was invalid and was ignored: {path}; {exception.Message}");
            return empty;
        }
    }

    private static Dictionary<string, string> ReadCatalog(Stream stream)
    {
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Language catalog root must be an object.");
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String ||
                string.IsNullOrEmpty(property.Name))
            {
                throw new InvalidDataException("Language catalog entries must map non-empty keys to strings.");
            }

            result[property.Name] = property.Value.GetString() ?? string.Empty;
        }

        return result;
    }

    [GeneratedRegex("\\{([0-9]+)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    // A translation may choose a noun form by a captured number: "{0|minuta|minuty|minut}"
    // inserts only the one/few/many word for the value of {0} under the language's rule.
    [GeneratedRegex("\\{([0-9]+)\\|([^{}|]*)\\|([^{}|]*)\\|([^{}|]*)\\}", RegexOptions.CultureInvariant)]
    private static partial Regex PluralPattern();

    internal static string SelectPluralForm(string languageCode, string value, string one, string few, string many)
    {
        if (!long.TryParse(value.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
            return many;
        number = Math.Abs(number);
        var lastDigit = number % 10;
        var lastTwoDigits = number % 100;
        var isFew = lastDigit is >= 2 and <= 4 && lastTwoDigits is < 12 or > 14;
        return languageCode switch
        {
            // Russian: 1, 21, 31 (not 11) take the singular.
            "ru" => lastDigit == 1 && lastTwoDigits != 11 ? one : isFew ? few : many,
            _ => number == 1 ? one : isFew ? few : many
        };
    }

    [GeneratedRegex("[A-Za-z]{3}", RegexOptions.CultureInvariant)]
    private static partial Regex GeneralTemplateWord();

    // "Mt. Nibel", "No. 1" and native "Мифрил. сабля" are labels, not two sentences.
    [GeneratedRegex(@"(?<!\b(?:Mt|No|St|Dr|Mr|Mrs|Ms|Jr|vs))[.!?]\s+(?![а-яё])", RegexOptions.CultureInvariant)]
    private static partial Regex ModSentenceBreak();

    [GeneratedRegex(@"[:,;]\s+", RegexOptions.CultureInvariant)]
    private static partial Regex ModClauseBreak();

    [GeneratedRegex(@"^[0-9]|[0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberAtEdge();

    private sealed record TemplateTranslation(
        string Source,
        Regex Pattern,
        IReadOnlyDictionary<int, string> GroupNames,
        int LiteralLength)
    {
        public bool SpansSentences { get; } = ModSentenceBreak().IsMatch(PlaceholderPattern().Replace(Source, "x"));

        public static TemplateTranslation Create(string source, bool guardNativeAbbreviations)
        {
            var groupNames = new Dictionary<int, string>();
            var pattern = new System.Text.StringBuilder("^");
            var literalLength = 0;
            var cursor = 0;
            var occurrence = 0;
            foreach (Match placeholder in PlaceholderPattern().Matches(source))
            {
                var literal = source[cursor..placeholder.Index];
                pattern.Append(guardNativeAbbreviations ? EscapeLiteral(literal) : Regex.Escape(literal));
                literalLength += literal.Length;
                var index = int.Parse(placeholder.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var groupName = $"p{index}_{occurrence++}";
                groupNames.TryAdd(index, groupName);
                pattern.Append("(?<").Append(groupName).Append(">.+?)");
                cursor = placeholder.Index + placeholder.Length;
            }

            var tail = source[cursor..];
            pattern.Append(guardNativeAbbreviations ? EscapeLiteral(tail) : Regex.Escape(tail)).Append('$');
            literalLength += tail.Length;
            return new TemplateTranslation(
                source,
                new Regex(pattern.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(20)),
                groupNames,
                literalLength);
        }

        // A literal sentence end never lands inside a native Russian abbreviation such as
        // "Критич. удар" (Deathblow): there the next word starts with a lowercase letter.
        private static string EscapeLiteral(string literal) =>
            Regex.Escape(literal).Replace(@"\.\ ", @"(?<!\b(?:Mt|No|St|Dr|Mr|Mrs|Ms|Jr|vs))\.\ (?![а-яё])");

        public string Apply(string target, Match match, Func<string, string> localizeCapture, string languageCode)
        {
            var withPlurals = PluralPattern().Replace(target, plural =>
            {
                var index = int.Parse(plural.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                return GroupNames.TryGetValue(index, out var groupName)
                    ? SelectPluralForm(languageCode, match.Groups[groupName].Value, plural.Groups[2].Value, plural.Groups[3].Value, plural.Groups[4].Value)
                    : plural.Groups[4].Value;
            });
            return PlaceholderPattern().Replace(withPlurals, placeholder =>
            {
                var index = int.Parse(placeholder.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                return GroupNames.TryGetValue(index, out var groupName)
                    ? localizeCapture(match.Groups[groupName].Value)
                    : placeholder.Value;
            });
        }
    }
}
