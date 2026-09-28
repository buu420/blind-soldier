using Ff7.Accessibility.Reloaded;

namespace Ff7.Accessibility.Reloaded.Tests;

internal static class NativeAskChoicePageTests
{
    public static void Run()
    {
        // Native niv_w / mansin1 layouts: the selector belongs to the FF-terminated
        // final page, regardless of the optional E0 indentation control byte.
        var pages = new[]
        {
            Page("Cloud", "Then..."),
            Page("Let's stay here for a while", "Let's go!")
        };
        Require(FieldAskTextFormatter.TryResolveChoicePage(pages, 0, 1, out var lines), "unindented multi-page ASK resolves");
        Equal("Let's stay here for a while", FieldAskTextFormatter.GetChoice(lines, 0, 1, 0), "selectable final-page row");
        Require(!FieldAskTextFormatter.IsChoicePageVisible(lines, "Cloud Then..."), "leading page cannot expose later choices");
        Require(FieldAskTextFormatter.IsChoicePageVisible(lines, "Let's stay here for a while Let's go!"), "only the complete choice page is visible");

        var mansion = new[]
        {
            Page("The letter continues.", "A second line."),
            Page("There seems to be another letter.", "Read it", "Ignore it")
        };
        Require(FieldAskTextFormatter.TryResolveChoicePage(mansion, 1, 2, out lines), "Mansion read-letter layout");
        Equal("There seems to be another letter.", FieldAskTextFormatter.FormatPrompt(lines, 1, 2), "question from the actual choice page");
        Equal("Ignore it", FieldAskTextFormatter.GetChoice(lines, 1, 2, 2), "Mansion second choice");

        var hints = new[] { Page("Instructions for the safe."), Page("dial (1)", "dial (2)", "dial (3)", "") };
        Require(FieldAskTextFormatter.TryResolveChoicePage(hints, 0, 3, out lines), "native blank selectable row is retained");
        Equal("Blank", FieldAskTextFormatter.GetChoice(lines, 0, 3, 3), "blank does not reveal hidden content");
        Equal("", FieldAskTextFormatter.GetChoice(lines, 0, 3, 4), "out-of-range is not a blank choice");
        Require(!FieldAskTextFormatter.TryResolveChoicePage(hints, 0, 4, out _), "invalid native row range rejected");

        var earlierIndented = new[]
        {
            new Ff7DecodedTextPage(new[] { new Ff7DecodedTextLine("Earlier text", true), new Ff7DecodedTextLine("More earlier text", true) }),
            Page("Yes", "No")
        };
        Require(FieldAskTextFormatter.TryResolveChoicePage(earlierIndented, 0, 1, out lines), "indentation is formatting, not selector ownership");
        Equal("Yes", FieldAskTextFormatter.GetChoice(lines, 0, 1, 0), "cannot select an earlier indented page");

        // FUN_00631945 handles both E8 and E9 as phase 14 (new page wait).
        var alternateBreak = Ff7EncodedTextDecoder.DecodePages(new byte[] { 0x21, 0xe9, 0x22, 0xff });
        Equal(2, alternateBreak.Count, "E9 preserves the native page boundary");
        Equal("B", alternateBreak[1].Lines[0].Text, "E9 final page text");
        Console.WriteLine("PASS native ASK pages: unindented choices, Mansion letters, blank safe row, and no early-page choices.");
    }

    private static Ff7DecodedTextPage Page(params string[] lines) =>
        new(lines.Select(line => new Ff7DecodedTextLine(line, false)).ToArray());
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Equal<T>(T expected, T actual, string message) =>
        Require(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}: expected {expected}, got {actual}");
}
