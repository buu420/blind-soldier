using System.Reflection;
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
    private readonly IReadOnlyList<TemplateTranslation> templates;
    private readonly Action<string>? log;
    private readonly HashSet<string> loggedFallbacks = new(StringComparer.Ordinal);

    private BlindSoldierLocalizer(
        Ff7GameLanguageDescriptor language,
        IReadOnlyDictionary<string, string> english,
        IReadOnlyDictionary<string, string> localized,
        Action<string>? log)
    {
        this.language = language;
        this.english = english;
        this.localized = localized;
        this.log = log;
        templates = english.Keys
            .Concat(localized.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(source => PlaceholderPattern().IsMatch(source))
            .Select(source => TemplateTranslation.Create(source))
            .OrderByDescending(template => template.LiteralLength)
            .ToArray();
    }

    public string LanguageCode => language.Code;

    public static BlindSoldierLocalizer Create(
        Ff7GameLanguageDescriptor language,
        string? modDirectory,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(language);
        var english = LoadEmbedded("en");
        var localized = language.Language == Ff7GameLanguage.English
            ? new Dictionary<string, string>(english, StringComparer.Ordinal)
            : LoadEmbedded(language.Code);

        if (!string.IsNullOrWhiteSpace(modDirectory))
        {
            var overridePath = Path.Combine(modDirectory, "Languages", $"{language.Code}.json");
            foreach (var pair in LoadOverride(overridePath, log))
            {
                localized[pair.Key] = pair.Value;
            }
        }

        return new BlindSoldierLocalizer(language, english, localized, log);
    }

    internal static BlindSoldierLocalizer CreateForTesting(
        Ff7GameLanguageDescriptor language,
        IReadOnlyDictionary<string, string> english,
        IReadOnlyDictionary<string, string> localized,
        Action<string>? log = null) =>
        new(language, english, localized, log);

    public string Localize(string text)
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

        foreach (var template in templates)
        {
            var match = template.Pattern.Match(text);
            if (!match.Success)
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

            return template.Apply(target, match, LocalizeCapturedValue);
        }

        return text;
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
    {
        var assembly = typeof(BlindSoldierLocalizer).Assembly;
        var suffix = $".Localization.{code}.json";
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

    private sealed record TemplateTranslation(
        string Source,
        Regex Pattern,
        IReadOnlyDictionary<int, string> GroupNames,
        int LiteralLength)
    {
        public static TemplateTranslation Create(string source)
        {
            var groupNames = new Dictionary<int, string>();
            var pattern = new System.Text.StringBuilder("^");
            var literalLength = 0;
            var cursor = 0;
            var occurrence = 0;
            foreach (Match placeholder in PlaceholderPattern().Matches(source))
            {
                var literal = source[cursor..placeholder.Index];
                pattern.Append(Regex.Escape(literal));
                literalLength += literal.Length;
                var index = int.Parse(placeholder.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var groupName = $"p{index}_{occurrence++}";
                groupNames.TryAdd(index, groupName);
                pattern.Append("(?<").Append(groupName).Append(">.+?)");
                cursor = placeholder.Index + placeholder.Length;
            }

            var tail = source[cursor..];
            pattern.Append(Regex.Escape(tail)).Append('$');
            literalLength += tail.Length;
            return new TemplateTranslation(
                source,
                new Regex(pattern.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline),
                groupNames,
                literalLength);
        }

        public string Apply(string target, Match match, Func<string, string> localizeCapture) =>
            PlaceholderPattern().Replace(target, placeholder =>
            {
                var index = int.Parse(placeholder.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                return GroupNames.TryGetValue(index, out var groupName)
                    ? localizeCapture(match.Groups[groupName].Value)
                    : placeholder.Value;
            });
    }
}
