using System.Diagnostics;
using System.Text.RegularExpressions;
using Ff7.Accessibility.Reloaded;
#if !BLIND_SOLDIER_LEGACY_TESTS
using System.Runtime.InteropServices;
using Ff7.Accessibility.Steam2026X64.Runtime;
#endif

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class RussianGeneratedSpeechIntegrationTests
{
    public static void Run()
    {
        var localizer = BlindSoldierLocalizer.Create(Ff7GameLanguages.RussianFanTranslation, null);
        (string Input, string Keep, string[] Remove)[] cases =
        [
            ("Battle. Enemies: Страж А and Страж Б.", "Страж Б", ["Battle", "Enemies", " and "]),
            ("Клауд. HP 314 of 314. MP 54 of 54", "Клауд", ["HP", "MP", " of "]),
            ("Клауд HP 314 of 314, MP 54 of 54, limit 30 percent.", "Клауд", ["limit", "percent", " of "]),
            ("Shooting Coaster. sight left 3, up 2. charge 4 of 8.", "", ["Shooting", "sight", "left", "up", "charge", " of "]),
            ("Hull 80 percent. Depth 120.", "", ["Hull", "percent", "Depth"]),
            ("Weapon materia slot 2, Огонь. Огненный урон по одному врагу.", "Огненный урон по одному врагу", ["Weapon", "materia slot"]),
            ("Items available: Зелье x2, Эфир.", "Эфир", ["Items available"]),
            ("Objects, Критич. удар Materia. up 3.", "Критич. удар", ["Objects", "Materia", "up"]),
            ("Weapon, Мифрил. сабля. Attack 18.", "Мифрил. сабля", ["Weapon", "Attack"]),
            ("Story, Cross the bridge toward Mt. Nibel. up 3.", "", ["Story", "Cross", "bridge", "Mt.", "Nibel", "up"]),
            ("Current name: Клауд. Character grid: прописная З.", "прописная З", ["Current name", "Character grid"]),
            ("Зелье x5. Восстанавливает 100 HP.", "Восстанавливает", ["HP"]),
            ("Left Midgar Area. Entered Grasslands Area. Entered grass.", "", ["Left", "Midgar", "Entered", "Grasslands", "grass"]),
            ("Navigation on. Emerald Weapon. left 24.", "", ["Navigation", "Emerald", "Weapon", "left"]),
            ("Mod settings. Navigation: Auto walk, on. 1 of 80.", "", ["Mod settings", "Navigation", "Auto walk", " of "]),
            ("Auto walk stopped. Could not get closer to Temple of the Ancients. Navigation is still on.", "", ["Auto walk", "Could not", "Temple of the Ancients", "Navigation"])
        ];
        var watch = Stopwatch.StartNew();
        var failures = new List<string>();
        foreach (var sample in cases)
        {
            var actual = localizer.LocalizeModSpeech(sample.Input);
            if (!Regex.IsMatch(actual, "[А-Яа-яЁё]") ||
                sample.Keep.Length > 0 && !actual.Contains(sample.Keep, StringComparison.Ordinal) ||
                sample.Remove.Any(s => actual.Contains(s, StringComparison.Ordinal)))
                failures.Add($"Incomplete Russian generated speech: {sample.Input}\n{actual}");
            var numbers = Regex.Matches(sample.Input, @"\d+").Select(m => m.Value).ToHashSet();
            if (!numbers.SetEquals(Regex.Matches(actual, @"\d+").Select(m => m.Value)))
                failures.Add($"Russian speech lost a displayed number: {sample.Input}\n{actual}");
        }
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("\n", failures));
        const string native = "Cloud: Lifestream.";
        Equal(native, localizer.Localize(native), "native dialogue stays strict");
        Equal("Attack 18", localizer.Localize("Attack 18"), "runtime-only patterns cannot rewrite native lines");
        var choice = localizer.Localize(Ff7.Accessibility.Core.DialogueChoiceSpeech.FormatOpening("Трава Силкис, 3 available", 1, 9));
        if (!choice.Contains("Трава Силкис", StringComparison.Ordinal) || choice.Contains("available", StringComparison.Ordinal) || choice.Contains("choice", StringComparison.Ordinal) || choice.Contains(" of ", StringComparison.Ordinal))
            throw new InvalidOperationException("Native choice metadata was not localized: " + choice);
        Equal("Нет, недоступно", localizer.Localize("Нет unavailable"), "disabled native choice after the opening announcement");
        var french = BlindSoldierLocalizer.Create(Ff7GameLanguages.Get(Ff7GameLanguage.French), null);
        foreach (var sample in cases) Equal(french.Localize(sample.Input), french.LocalizeModSpeech(sample.Input), "existing French behavior");
#if BLIND_SOLDIER_LEGACY_TESTS
        Equal(native, Mod.LocalizePieceForNativeLine(localizer, native, generated: false), "x86 native piece boundary");
        Equal(localizer.LocalizeModSpeech(cases[4].Input), Mod.LocalizePieceForNativeLine(localizer, cases[4].Input, generated: true), "x86 generated piece boundary");
#else
        var spoken = new List<string>();
        using var speaker = new PrismNativeSpeaker(_ => { }, (nint)1, (nint)1,
            (_, text, _) => { spoken.Add(Marshal.PtrToStringUTF8(text)!); return PrismError.Ok; }, _ => { });
        using var output = new Steam2026ResearchAccessibilityOutput(speaker, localizer, _ => { });
        output.SpeakModSpeech(cases[4].Input, false);
        Equal(localizer.LocalizeModSpeech(cases[4].Input), spoken[^1], "Russian generated text at x64 Prism boundary");
        if (!output.RepeatLast()) throw new InvalidOperationException("Russian repeat was not delivered.");
        Equal(spoken[0], spoken[1], "repeat stores Russian output");
        output.Speak(native, true); Equal(native, spoken[^1], "x64 native dialogue boundary");
        if (localizer.UseRecordedAudioDescriptions) throw new InvalidOperationException("Russian must bypass English recordings.");
#endif
        Console.WriteLine($"PASS Russian composed menu, battle, minigame, naming, navigation and native speech boundaries ({watch.ElapsedMilliseconds} ms).");
    }

    private static void Equal(string expected, string actual, string context)
    { if (expected != actual) throw new InvalidOperationException($"{context}: {actual}"); }
}
