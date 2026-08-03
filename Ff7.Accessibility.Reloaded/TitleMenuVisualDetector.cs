using System.Drawing;

namespace Ff7.Accessibility.Reloaded;

public sealed class TitleMenuVisualDetector
{
    public TitleMenuDetection? Detect(Bitmap bitmap)
        => Detect(bitmap.Width, bitmap.Height, bitmap.GetPixel);

    internal TitleMenuDetection? Detect(int width, int height, Func<int, int, Color> getPixel)
    {
        if (!LooksLikeTitleMenu(width, height, getPixel))
        {
            return null;
        }

        var newGameScore = ScoreNeutralBrightRegion(width, height, getPixel, 258, 242, 62, 26);
        var continueScore = ScoreNeutralBrightRegion(width, height, getPixel, 258, 276, 62, 26);
        var minimumCursorScore = Math.Max(28, width * height / 95000);

        if (newGameScore < minimumCursorScore && continueScore < minimumCursorScore)
        {
            return null;
        }

        return newGameScore >= continueScore
            ? new TitleMenuDetection("New Game", newGameScore, continueScore)
            : new TitleMenuDetection("Continue", newGameScore, continueScore);
    }

    private static bool LooksLikeTitleMenu(int width, int height, Func<int, int, Color> getPixel)
    {
        var newGameTextScore = ScoreNeutralBrightRegion(width, height, getPixel, 325, 238, 160, 36);
        var continueTextScore = ScoreNeutralBrightRegion(width, height, getPixel, 325, 270, 160, 36);
        var versionTextScore = ScoreNeutralBrightRegion(width, height, getPixel, 4, 568, 175, 28);
        var darkBackgroundScore = ScoreDarkRegion(width, height, getPixel, 500, 220, 180, 160);

        var minNewGameScore = Math.Max(140, width * height / 3300);
        var minContinueScore = Math.Max(35, width * height / 17000);
        var minVersionScore = Math.Max(30, width * height / 18000);
        var minDarkBackgroundScore = ScaledArea(width, height, 180, 160) * 3 / 4;

        return newGameTextScore >= minNewGameScore
            && continueTextScore >= minContinueScore
            && versionTextScore >= minVersionScore
            && darkBackgroundScore >= minDarkBackgroundScore;
    }

    private static int ScoreNeutralBrightRegion(
        int bitmapWidth,
        int bitmapHeight,
        Func<int, int, Color> getPixel,
        int x,
        int y,
        int width,
        int height)
    {
        var sx = bitmapWidth / 800.0;
        var sy = bitmapHeight / 600.0;
        var left = Clamp((int)Math.Round(x * sx), 0, bitmapWidth - 1);
        var top = Clamp((int)Math.Round(y * sy), 0, bitmapHeight - 1);
        var right = Clamp((int)Math.Round((x + width) * sx), left + 1, bitmapWidth);
        var bottom = Clamp((int)Math.Round((y + height) * sy), top + 1, bitmapHeight);
        var score = 0;

        for (var py = top; py < bottom; py++)
        {
            for (var px = left; px < right; px++)
            {
                var pixel = getPixel(px, py);
                var max = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
                var min = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                if (max > 105 && max - min < 75)
                {
                    score++;
                }
            }
        }

        return score;
    }

    private static int ScoreDarkRegion(
        int bitmapWidth,
        int bitmapHeight,
        Func<int, int, Color> getPixel,
        int x,
        int y,
        int width,
        int height)
    {
        var sx = bitmapWidth / 800.0;
        var sy = bitmapHeight / 600.0;
        var left = Clamp((int)Math.Round(x * sx), 0, bitmapWidth - 1);
        var top = Clamp((int)Math.Round(y * sy), 0, bitmapHeight - 1);
        var right = Clamp((int)Math.Round((x + width) * sx), left + 1, bitmapWidth);
        var bottom = Clamp((int)Math.Round((y + height) * sy), top + 1, bitmapHeight);
        var score = 0;

        for (var py = top; py < bottom; py++)
        {
            for (var px = left; px < right; px++)
            {
                var pixel = getPixel(px, py);
                if (pixel.R < 50 && pixel.G < 50 && pixel.B < 50)
                {
                    score++;
                }
            }
        }

        return score;
    }

    private static int ScaledArea(int bitmapWidth, int bitmapHeight, int width, int height)
        => Math.Max(1, (int)Math.Round(width * (bitmapWidth / 800.0)) * (int)Math.Round(height * (bitmapHeight / 600.0)));

    private static int Clamp(int value, int min, int max)
        => value < min ? min : value > max ? max : value;
}

public sealed record TitleMenuDetection(string Item, int NewGameCursorScore, int ContinueCursorScore);
