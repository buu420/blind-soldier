using System.Globalization;
using System.Text;

namespace Ff7.Accessibility.Reloaded;

public sealed class MenuTextRenderDiagnostics
{
    private readonly TimeSpan deduplicationWindow;
    private readonly Func<DateTime> now;
    private readonly Dictionary<string, DateTime> lastSeenByKey = new(StringComparer.Ordinal);

    public MenuTextRenderDiagnostics()
        : this(TimeSpan.FromMilliseconds(750), () => DateTime.UtcNow)
    {
    }

    public MenuTextRenderDiagnostics(TimeSpan deduplicationWindow, Func<DateTime> now)
    {
        this.deduplicationWindow = deduplicationWindow;
        this.now = now;
    }

    public bool TryCreateEntry(string? rawText, uint x, uint y, int color, int context, out MenuTextRenderEntry entry)
    {
        entry = default;
        var text = NormalizeText(rawText);
        if (text.Length == 0)
        {
            return false;
        }

        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{text}\u001f{x}\u001f{y}\u001f{color}\u001f{context}");
        var current = now();
        if (lastSeenByKey.TryGetValue(key, out var lastSeen) && current - lastSeen < deduplicationWindow)
        {
            return false;
        }

        lastSeenByKey[key] = current;
        entry = new MenuTextRenderEntry(text, x, y, color, context);
        return true;
    }

    private static string NormalizeText(string? rawText)
    {
        if (string.IsNullOrEmpty(rawText))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(rawText.Length);
        foreach (var ch in rawText)
        {
            if (ch == '\0')
            {
                break;
            }

            if (char.IsWhiteSpace(ch))
            {
                builder.Append(' ');
            }
            else if (!char.IsControl(ch))
            {
                builder.Append(ch);
            }
        }

        return CollapseWhitespace(builder.ToString().Trim());
    }

    private static string CollapseWhitespace(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var previousWasSpace = false;
        foreach (var ch in text)
        {
            if (ch == ' ')
            {
                if (!previousWasSpace)
                {
                    builder.Append(ch);
                }

                previousWasSpace = true;
                continue;
            }

            builder.Append(ch);
            previousWasSpace = false;
        }

        return builder.ToString();
    }
}
