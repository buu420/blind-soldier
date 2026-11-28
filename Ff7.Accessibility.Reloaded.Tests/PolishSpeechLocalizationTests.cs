using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ff7.Accessibility.Core;
using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

/// <summary>
/// The Polish fan translation uses the composed mod-speech path the Russian one introduced:
/// generated settings and navigation lines are localized by parts from pl.runtime.json and
/// pl.composition.json, while native Polish dialogue keeps the strict lookup.
/// </summary>
internal static class PolishSpeechLocalizationTests
{
    public static void Run()
    {
        var localizer = BlindSoldierLocalizer.Create(Ff7GameLanguages.PolishFanTranslation, null);
        ValidateCatalogs();
        Equal(true, localizer.ComposesModSpeech, "Polish composes mod speech");
        foreach (var runtime in new[] { ModSettingsRuntime.Legacy, ModSettingsRuntime.Steam2026 })
        foreach (var device in new[] { ModSettingsInputDevice.Keyboard, ModSettingsInputDevice.Controller })
        {
            var config = new AccessibilityConfig();
            var store = PlayerSettingsStore.Open(null, config, runtime);
            var menu = new ModSettingsMenu(config, store, () => "Descriptions reset.",
                resetAllDescriptions: () => "Descriptions reset.");
            var opening = localizer.LocalizeModSpeech(menu.Open(device));
            RequireNoEnglish(opening, $"{runtime} {device} opening", "move through settings", "Mod settings");
            for (var i = 0; i < store.Catalogue.Entries.Count; i++)
            {
                var label = menu.Current.Label;
                var spoken = localizer.LocalizeModSpeech(menu.Handle(ModSettingsMenuCommand.Repeat));
                if (Regex.IsMatch(label, "[A-Za-z]{3}") && spoken.Contains(label, StringComparison.Ordinal) &&
                    localizer.LocalizeModSpeech(label) != label)
                    throw new InvalidOperationException($"Polish setting label remains English: {label}; {spoken}");
                RequireNoEnglish(spoken, $"{runtime} {label}", " percent", ", on.", ", off.");
                if (Regex.IsMatch(spoken, @"\b\d+ of \d+\b"))
                    throw new InvalidOperationException($"Polish setting count remains English: {spoken}");
                if (menu.Current.Kind == ModSettingKind.Action)
                {
                    RequireNoEnglish(localizer.LocalizeModSpeech(menu.Handle(ModSettingsMenuCommand.Activate)), "reset confirmation", "Press ");
                    RequireNoEnglish(localizer.LocalizeModSpeech(menu.Handle(ModSettingsMenuCommand.Activate)), "reset result", "Descriptions reset", " reset ");
                }

                _ = menu.Handle(ModSettingsMenuCommand.Next);
            }

            RequireNoEnglish(localizer.LocalizeModSpeech(menu.Close()), "settings close", "closed");
        }

        // Field navigation composes a category, a target label and a route cue.
        Equal("Fabuła, Barret. góra 3.", localizer.LocalizeModSpeech("Story, Barret. up 3."), "category, native name and route cue");
        Equal("Fabuła, Idź za Barretem w stronę windy. lewo 2.",
            localizer.LocalizeModSpeech("Story, Follow Barret toward the elevator. left 2."), "story target label");
        foreach (var category in new[] { "Story", "NPCs", "Objects", "Exits" })
        {
            var spoken = localizer.LocalizeModSpeech($"{category}: none for this field yet.");
            RequireNoEnglish(spoken, $"{category} without targets", category + ":", "none for this field");
        }

        Equal("ostatnio widziano 2 sekundy temu", localizer.LocalizeModSpeech("last seen 2 seconds ago"), "Polish plural: few");
        Equal("ostatnio widziano 5 sekund temu", localizer.LocalizeModSpeech("last seen 5 seconds ago"), "Polish plural: many");
        Equal("ostatnio widziano 1 sekundę temu", localizer.LocalizeModSpeech("last seen 1 second ago"), "Polish plural: one");

        // Native Polish text, and names the Polish game keeps in English, stay as the game shows them.
        foreach (var native in new[]
                 {
                     "Cloud: Lifestream.", "Co to jest?", "Czy to Hojo?", "Zrobię to za 3000.", "No, to chodźmy.",
                     "Magic Plus", "Spirit Lance", "Black Materia", "Fire", "up to the end of the Planet…"
                 })
            Equal(native, localizer.Localize(native), $"native text \"{native}\"");
        Console.WriteLine("PASS Polish speech catalogs, every settings entry and both control schemes on both runtime policies.");
    }

    private static void ValidateCatalogs()
    {
        var assembly = typeof(BlindSoldierLocalizer).Assembly;
        Dictionary<string, string> Read(string suffix)
        {
            var name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Missing Polish UI catalog: {suffix}");
            using var stream = assembly.GetManifestResourceStream(name)!;
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
        }

        var english = Read(".Localization.en.json");
        var polish = Read(".Localization.pl.json");
        if (!english.Keys.ToHashSet().SetEquals(polish.Keys)) throw new InvalidOperationException("Incomplete base Polish UI catalog.");
        var composition = Read(".Localization.pl.composition.json");
        if (!Read(".Localization.ru.composition.json").Keys.ToHashSet().SetEquals(composition.Keys))
            throw new InvalidOperationException("The Polish composition catalog does not mirror the trusted composition entries.");
        foreach (var suffix in new[] { ".Localization.pl.json", ".Localization.pl.runtime.json", ".Localization.pl.composition.json" })
        foreach (var (key, value) in Read(suffix))
        {
            var plurals = Regex.Matches(value, @"\{(\d+)\|[^{}|]*\|[^{}|]*\|[^{}|]*\}").Select(m => "{" + m.Groups[1].Value + "}");
            if (string.IsNullOrWhiteSpace(value) ||
                !Placeholders(value).ToHashSet().IsSubsetOf(Placeholders(key)) ||
                !plurals.ToHashSet().IsSubsetOf(Placeholders(key)))
                throw new InvalidOperationException($"Invalid Polish translation placeholders: {key}");
        }
    }

    private static IEnumerable<string> Placeholders(string text) => Regex.Matches(text, @"\{\d+\}")
        .Select(match => match.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);

    private static void RequireNoEnglish(string text, string label, params string[] englishFragments)
    {
        foreach (var fragment in englishFragments)
            if (text.Contains(fragment, StringComparison.Ordinal))
                throw new InvalidOperationException($"Polish {label} still says \"{fragment}\": {text}");
    }

    private static void Equal(object expected, object actual, string label)
    {
        if (!Equals(expected, actual)) throw new InvalidOperationException($"{label}: expected {expected}, got {actual}");
    }
}
