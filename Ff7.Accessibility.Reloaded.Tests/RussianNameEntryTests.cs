using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class RussianNameEntryTests
{
    public static void Run()
    {
        Ff7EncodedTextDecoder.SetDefaultLanguage(Ff7GameLanguages.RussianFanTranslation);
        try
        {
            var empty = Enumerable.Repeat((byte)255, 9).ToArray();
            var now = DateTime.UnixEpoch;
            string? Select(int column, int row)
            {
                var tracker = new NameEntryNativeNameTracker(TimeSpan.Zero);
                _ = tracker.Observe(true, 0, column, row, 0, 0, empty, now);
                return tracker.Observe(true, 0, column, row, 0, 0, empty, now);
            }
            Contains("прописная А", Select(0, 0), "first Russian naming letter");
            Contains("прописная З", Select(7, 0), "Russian alphabet's shared З/3 glyph");
            Contains("строчная я", Select(9, 5), "Russian row five has ten available letters");
            Contains("Character grid: 3.", Select(3, 6), "the numeric row still says three");
#if BLIND_SOLDIER_LEGACY_TESTS
            var drawn = new NameEntryMenuSpeechTracker(TimeSpan.Zero);
            drawn.ObserveText(new MenuTextRenderEntry("3", 264, 214, -1, 0), 5, now);
            Contains("прописная З", drawn.ObserveCursor(new("test", 5, 264, 214, 0), now)?.SpokenText,
                "a drawn shared glyph keeps its alphabet-cell meaning");
            var commands = new NameEntryMenuSpeechTracker(TimeSpan.Zero);
            commands.ObserveText(new MenuTextRenderEntry("Пробел", 450, 214, -1, 0), 5, now);
            commands.ObserveText(new MenuTextRenderEntry("Удалить", 450, 248, -1, 0), 5, now);
            Contains("строчная я", commands.ObserveCursor(new("test", 5, 330, 384, 0), now)?.SpokenText,
                "translated name commands establish the current naming screen");
#endif
        }
        finally { Ff7EncodedTextDecoder.SetDefaultLanguage(Ff7GameLanguages.Get(Ff7GameLanguage.English)); }
        if (!NameEntryCharacterTable.TryGet(0, 0, out var english) || english != "capital A")
            throw new InvalidOperationException("Russian naming changed the English alphabet.");
        Console.WriteLine("PASS Russian native naming alphabet, shared glyphs, translated commands and English isolation.");
    }

    private static void Contains(string expected, string? actual, string label)
    {
        if (actual?.Contains(expected, StringComparison.Ordinal) != true)
            throw new InvalidOperationException($"{label}: expected {expected}, got {actual ?? "<unavailable>"}.");
    }
}
