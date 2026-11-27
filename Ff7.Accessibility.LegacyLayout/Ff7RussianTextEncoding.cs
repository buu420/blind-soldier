using System.Text.RegularExpressions;

namespace Ff7.Accessibility.Reloaded;

/// <summary>The replacement glyph slots in FFRTT/ayumosa Russian 7H 1.02; not CP1251.</summary>
internal static partial class Ff7RussianTextEncoding
{
    // Read from the supplied menu_us.lgp usfont_{a,b}_h.tex and checked against
    // Hext Text.txt and kernel2.bin. Homoglyphs have both original Latin slots
    // and replacement extended slots. The renderer uses the same glyph byte
    // for menus and field text (006E706D), with the ordinary control codes.
    public static bool TryReadCharacter(byte value, out char character)
    {
        character = value switch
        {
            0x03 => 'П', 0x04 => 'н', 0x06 => 'м', 0x0a => 'п',
            0x1c => 'э', 0x1d => 'ч', 0x1e => 'ю', 0x20 => 'Б',
            0x21 => 'А', 0x22 => 'В', 0x23 => 'С', 0x25 => 'Е',
            0x28 => 'Н', 0x2b => 'К', 0x2d => 'М', 0x2f => 'О',
            0x30 => 'Р', 0x34 => 'Т', 0x38 => 'Х', 0x41 => 'а',
            0x43 => 'с', 0x45 => 'е', 0x4f => 'о', 0x50 => 'р',
            0x58 => 'х', 0x59 => 'у', 0x5e => 'ё', 0x63 => 'Й',
            0x65 => 'Ц', 0x66 => 'Ь', 0x67 => 'б', 0x68 => 'а',
            0x69 => 'в', 0x6a => 'д', 0x6b => 'г', 0x6c => 'е',
            0x6d => 'з', 0x6e => 'й', 0x6f => 'и', 0x70 => 'к',
            0x71 => 'л', 0x72 => '.', 0x73 => ':', 0x74 => 'о', 0x75 => 'п', 0x76 => 'с',
            0x77 => 'у', 0x78 => 'т', 0x79 => 'ф', 0x7a => 'ц',
            0x7b => 'х', 0x7c => 'ъ', 0x7d => 'щ', 0x7e => 'ы',
            0x7f => 'ь', 0x84 => 'Щ', 0x85 => 'Ы', 0x87 => 'Я',
            0x8e => 'Ж', 0x8f => 'Ш', 0x9e => 'ж', 0x9f => 'ш',
            0xab => 'А', 0xac => 'Г', 0xad => 'Ю', 0xb7 => 'Ё',
            0xb8 => 'я', 0xc5 => 'Э', 0xc6 => 'К', 0xc7 => 'Л',
            0xc8 => 'Л', 0xc9 => 'И', 0xca => 'Н', 0xcb => 'О',
            0xcc => 'П', 0xcd => 'М', 0xce => 'У', 0xcf => 'Ф',
            0xd1 => 'Д', 0xd2 => 'Щ', 0xd3 => 'Ы', _ => '\0'
        };
        return character != '\0';
    }

    public static string NormalizeGlyphWords(string text) => WordPattern().Replace(text, match =>
    {
        var word = match.Value;
        // The patch still displays Latin titles/abbreviations. Restore shared
        // Latin shapes only inside an otherwise Latin word, not Russian words.
        var latin = word.Any(char.IsAsciiLetter);
        var distinctCyrillic = word.Any(c => IsCyrillic(c) && LatinShape(c) == c);
        if ((latin && !distinctCyrillic) || word is "НР" or "МР" or "АР")
            return new string(word.Select(LatinShape).ToArray());

        // FFRTT also uses 3/4 for the shared З/Ч shapes (Зелье, Чокобо,
        // ПЕРЕКЛЮЧИТЬ). Spell suffixes and standalone values remain digits.
        if (word.Length > 2 && word[0] == '3' && IsCyrillic(word[1]) && IsCyrillic(word[2]))
            word = "З" + word[1..];
        if (word.Length > 2 && word[0] == '4' && IsCyrillic(word[1]) && IsCyrillic(word[2]))
            word = "Ч" + word[1..];
        return InternalChePattern().Replace(InternalZePattern().Replace(word, "З"), "Ч");
    });

    internal static string LocalizeButtonToken(string text) => text
        .Replace("[OK]", "[Подтвердить]", StringComparison.Ordinal)
        .Replace("[MENU]", "[Меню]", StringComparison.Ordinal)
        .Replace("[CANCEL]", "[Отмена]", StringComparison.Ordinal)
        .Replace("[SWITCH]", "[Переключить]", StringComparison.Ordinal)
        .Replace("[UP]", "[Вверх]", StringComparison.Ordinal)
        .Replace("[DOWN]", "[Вниз]", StringComparison.Ordinal)
        .Replace("[LEFT]", "[Влево]", StringComparison.Ordinal)
        .Replace("[RIGHT]", "[Вправо]", StringComparison.Ordinal);

    private static bool IsCyrillic(char c) => c is >= 'А' and <= 'я' or 'Ё' or 'ё';

    private static char LatinShape(char c) => c switch
    {
        'А' => 'A', 'В' => 'B', 'С' => 'C', 'Е' => 'E', 'Н' => 'H', 'К' => 'K',
        'М' => 'M', 'О' => 'O', 'Р' => 'P', 'Т' => 'T', 'Х' => 'X',
        'а' => 'a', 'с' => 'c', 'е' => 'e', 'о' => 'o', 'р' => 'p', 'х' => 'x', 'у' => 'y',
        _ => c
    };

    [GeneratedRegex(@"[A-Za-zА-Яа-яЁё0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"(?<=[А-ЯЁ])3(?=[А-ЯЁ])", RegexOptions.CultureInvariant)]
    private static partial Regex InternalZePattern();

    [GeneratedRegex(@"(?<=[А-ЯЁ])4(?=[А-ЯЁ])", RegexOptions.CultureInvariant)]
    private static partial Regex InternalChePattern();
}
