using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class RussianSpeechLocalizationTests
{
    public static void Run()
    {
        var localizer = BlindSoldierLocalizer.Create(Ff7GameLanguages.RussianFanTranslation, null);
        ValidateCatalogs();
        foreach (var runtime in new[] { ModSettingsRuntime.Legacy, ModSettingsRuntime.Steam2026 })
        foreach (var device in new[] { ModSettingsInputDevice.Keyboard, ModSettingsInputDevice.Controller })
        {
            var config = new AccessibilityConfig();
            var store = PlayerSettingsStore.Open(null, config, runtime);
            var menu = new ModSettingsMenu(config, store, () => "Descriptions reset.",
                resetAllDescriptions: () => "Descriptions reset.");
            var opening = localizer.LocalizeModSpeech(menu.Open(device));
            RequireCyrillic(opening, $"{runtime} {device} opening");
            if (opening.Contains("move through settings", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Russian settings controls were not translated.");
            for (var i = 0; i < store.Catalogue.Entries.Count; i++)
            {
                var label = menu.Current.Label;
                var raw = menu.Handle(ModSettingsMenuCommand.Repeat);
                var spoken = localizer.LocalizeModSpeech(raw);
                RequireCyrillic(spoken, $"{runtime} {label}");
                if (Regex.IsMatch(label, "[A-Za-z]{3}") && spoken.Contains(label, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Russian setting label remains English: {label}; {spoken}");
                if (spoken.Contains(" percent", StringComparison.Ordinal) ||
                    Regex.IsMatch(spoken, @"\b\d+ of \d+\b") || spoken.Contains(", on.", StringComparison.Ordinal) ||
                    spoken.Contains(", off.", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Russian setting value/count remains English: {spoken}");
                if (menu.Current.Kind == ModSettingKind.Action)
                {
                    RequireCyrillic(localizer.LocalizeModSpeech(menu.Handle(ModSettingsMenuCommand.Activate)), "reset confirmation");
                    RequireCyrillic(localizer.LocalizeModSpeech(menu.Handle(ModSettingsMenuCommand.Activate)), "reset result");
                }
                _ = menu.Handle(ModSettingsMenuCommand.Next);
            }
            RequireCyrillic(localizer.LocalizeModSpeech(menu.Close()), "settings close");
        }
        const string native = "Cloud: Lifestream.";
        if (localizer.Localize(native) != native || localizer.Localize("Words of wisdom.") != "Words of wisdom.")
            throw new InvalidOperationException("Russian mod composition changed ordinary native dialogue.");
        Console.WriteLine("PASS Russian speech catalogs, every settings entry and both control schemes on both runtime policies.");
    }

    private static void ValidateCatalogs()
    {
        var assembly = typeof(BlindSoldierLocalizer).Assembly;
        Dictionary<string, string> Read(string suffix)
        {
            var name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Missing Russian UI catalog: {suffix}");
            using var stream = assembly.GetManifestResourceStream(name)!;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        }
        var english = Read(".Localization.en.json");
        var russian = Read(".Localization.ru.json");
        if (!english.Keys.ToHashSet().SetEquals(russian.Keys)) throw new InvalidOperationException("Incomplete base Russian UI catalog.");
        foreach (var suffix in new[] { ".Localization.ru.json", ".Localization.ru.runtime.json", ".Localization.ru.composition.json" })
        foreach (var (key, value) in Read(suffix))
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !Placeholders(key).SequenceEqual(Placeholders(value)))
                throw new InvalidOperationException($"Invalid Russian translation placeholders: {key}");
        }
    }

    private static IEnumerable<string> Placeholders(string text) => Regex.Matches(text, @"\{\d+\}")
        .Select(match => match.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    private static void RequireCyrillic(string text, string label)
    {
        if (!Regex.IsMatch(text, "[А-Яа-яЁё]"))
            throw new InvalidOperationException($"Missing Russian {label}: {text}");
    }
}
