namespace Ff7.Accessibility.Reloaded;

public static class TitleMenuCursorReader
{
    public const int TitleModule = 20;

    public static bool TryCreateSelection(TitleMenuCursorSnapshot snapshot, out TitleMenuCursorSelection selection)
    {
        selection = default;
        if (snapshot.CurrentModule != TitleModule)
        {
            return false;
        }

        if (!TryClassifyHighResolutionCursor(snapshot.X, snapshot.Y, out var text) &&
            !TryClassifyLowResolutionCursor(snapshot.X, snapshot.Y, out text))
        {
            return false;
        }

        selection = new TitleMenuCursorSelection(
            text,
            $"title-menu-cursor\u001f{text}",
            snapshot.Source,
            snapshot.CurrentModule,
            snapshot.X,
            snapshot.Y,
            snapshot.Context);
        return true;
    }

    public static bool LooksNearTitleMenu(TitleMenuCursorSnapshot snapshot) =>
        snapshot.CurrentModule == TitleModule &&
        snapshot.X is >= 70 and <= 285 &&
        snapshot.Y is >= 80 and <= 245;

    private static bool TryClassifyHighResolutionCursor(int x, int y, out string text)
    {
        text = string.Empty;
        if (x is < 180 or > 265)
        {
            return false;
        }

        if (y is >= 180 and <= 205)
        {
            text = "New Game";
            return true;
        }

        if (y is >= 206 and <= 235)
        {
            text = "Continue";
            return true;
        }

        return false;
    }

    private static bool TryClassifyLowResolutionCursor(int x, int y, out string text)
    {
        text = string.Empty;
        if (x is < 90 or > 135)
        {
            return false;
        }

        if (y is >= 90 and <= 103)
        {
            text = "New Game";
            return true;
        }

        if (y is >= 104 and <= 118)
        {
            text = "Continue";
            return true;
        }

        return false;
    }
}

public readonly record struct TitleMenuCursorSnapshot(
    string Source,
    int CurrentModule,
    int X,
    int Y,
    int Context);

public readonly record struct TitleMenuCursorSelection(
    string SpokenText,
    string Key,
    string Source,
    int CurrentModule,
    int X,
    int Y,
    int Context)
{
    public string ToLogLine() =>
        $"Title menu native cursor: {SpokenText} source={Source} module={CurrentModule} x={X} y={Y} context=0x{Context:X8}";
}
