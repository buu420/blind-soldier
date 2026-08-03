namespace Ff7.Accessibility.Reloaded;

internal static class NameEntryCharacterTable
{
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
        if (row < 0 || row >= Rows.Length || column < 0 || column >= Rows[row].Length)
        {
            text = string.Empty;
            return false;
        }

        text = Rows[row][column];
        return true;
    }
}
