namespace Ff7.Accessibility.Reloaded;

internal static class NameEntryCharacterTable
{
    // FFRTT 1.02 Text.txt, Naming3 at 0x00921D70. The alphabet's З/Ч
    // reuse the digit glyphs 3/4; their cell, not the isolated byte, gives
    // those two characters their meaning on the naming screen.
    private static readonly string[] RussianRows =
    [
        "АБВГДЕЖЗИЙ", "КЛМНОПРСТУ", "ФХЦЧШЩЭЮЯа", "бвгдежзийк",
        "лмнопрстуф", "хцчшщыьэюя", "0123456789"
    ];

    public static bool UsesRussianGrid => Ff7EncodedTextDecoder.DefaultTextEncodingProfile ==
        Ff7TextEncodingProfile.RussianFfrttTranslation;

    private static readonly string[][] Rows =
    [
        ["capital A", "capital B", "capital C", "capital D", "capital E", "capital F", "capital G", "capital H", "capital I", "capital J"],
        ["capital K", "capital L", "capital M", "capital N", "capital O", "capital P", "capital Q", "capital R", "capital S", "capital T"],
        ["capital U", "capital V", "capital W", "capital X", "capital Y", "capital Z", "comma", "period", "plus", "minus"],
        ["lowercase a", "lowercase b", "lowercase c", "lowercase d", "lowercase e", "lowercase f", "lowercase g", "lowercase h", "lowercase i", "lowercase j"],
        ["lowercase k", "lowercase l", "lowercase m", "lowercase n", "lowercase o", "lowercase p", "lowercase q", "lowercase r", "lowercase s", "lowercase t"],
        ["lowercase u", "lowercase v", "lowercase w", "lowercase x", "lowercase y", "lowercase z", "colon", "semicolon"],
        ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"]
    ];

    public static bool TryGet(int column, int row, out string text)
    {
        if (UsesRussianGrid)
        {
            if (!TryGetRussianCharacter(column, row, out var character))
            {
                text = string.Empty;
                return false;
            }
            text = char.IsUpper(character) ? $"прописная {character}" :
                char.IsLower(character) ? $"строчная {character}" : character.ToString();
            return true;
        }
        if (row < 0 || row >= Rows.Length || column < 0 || column >= Rows[row].Length)
        {
            text = string.Empty;
            return false;
        }

        text = Rows[row][column];
        return true;
    }

    public static bool TryDescribeRussianDrawnCell(int column, int row, string drawn, out string text)
    {
        text = string.Empty;
        if (!UsesRussianGrid || !TryGetRussianCharacter(column, row, out var character)) return false;
        var glyph = character switch { 'З' => '3', 'Ч' => '4', _ => character };
        if (!string.Equals(drawn.Trim(), glyph.ToString(), StringComparison.Ordinal)) return false;
        return TryGet(column, row, out text);
    }

    private static bool TryGetRussianCharacter(int column, int row, out char character)
    {
        character = default;
        if (row < 0 || row >= RussianRows.Length || column < 0 || column >= RussianRows[row].Length)
            return false;
        character = RussianRows[row][column];
        return true;
    }
}
