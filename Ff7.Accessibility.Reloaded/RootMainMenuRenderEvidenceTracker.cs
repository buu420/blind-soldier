namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Proves that the native root in-game menu is currently being rendered.
/// FFVII keeps the underlying field or world-map module active while that
/// overlay is open, so the module byte alone cannot own root-menu speech.
/// </summary>
public sealed class RootMainMenuRenderEvidenceTracker
{
    private const int RootMainMenuContext = 0x3A83126F;
    private const uint RootMenuX = 508;
    private const uint NativeRowSpacing = 26;
    private const int RequiredConsecutiveRows = 5;
    private readonly object sync = new();
    private readonly TimeSpan evidenceWindow;
    private readonly Dictionary<uint, RenderedRow> recentRows = new();
    private DateTime evidenceExpiresUtc = DateTime.MinValue;

    public RootMainMenuRenderEvidenceTracker(TimeSpan evidenceWindow)
    {
        if (evidenceWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(evidenceWindow));
        }

        this.evidenceWindow = evidenceWindow;
    }

    public void Observe(MenuTextRenderEntry entry, DateTime now)
    {
        if (now.Kind != DateTimeKind.Utc ||
            entry.Context != RootMainMenuContext ||
            entry.X != RootMenuX ||
            entry.Y > 4096 ||
            string.IsNullOrWhiteSpace(entry.Text) ||
            !entry.Text.Any(char.IsLetterOrDigit))
        {
            return;
        }

        lock (sync)
        {
            PruneExpiredRows(now);
            recentRows[entry.Y] = new RenderedRow(now, entry.Text.Trim());
            if (HasNativeRowRun())
            {
                evidenceExpiresUtc = now + evidenceWindow;
            }
        }
    }

    public bool IsActive(DateTime now)
    {
        if (now.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        lock (sync)
        {
            PruneExpiredRows(now);
            if (evidenceExpiresUtc == DateTime.MinValue)
            {
                return false;
            }

            if (now > evidenceExpiresUtc)
            {
                evidenceExpiresUtc = DateTime.MinValue;
                return false;
            }

            return true;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            evidenceExpiresUtc = DateTime.MinValue;
            recentRows.Clear();
        }
    }

    private void PruneExpiredRows(DateTime now)
    {
        foreach (var y in recentRows
                     .Where(pair => now - pair.Value.SeenAt > evidenceWindow)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            recentRows.Remove(y);
        }
    }

    private bool HasNativeRowRun()
    {
        var consecutiveRows = 0;
        var distinctTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        uint? previousY = null;
        foreach (var pair in recentRows.OrderBy(pair => pair.Key))
        {
            var y = pair.Key;
            if (previousY is not { } previous || y != previous + NativeRowSpacing)
            {
                consecutiveRows = 0;
                distinctTexts.Clear();
            }

            consecutiveRows++;
            distinctTexts.Add(pair.Value.Text);
            if (consecutiveRows >= RequiredConsecutiveRows &&
                distinctTexts.Count >= RequiredConsecutiveRows)
            {
                return true;
            }

            previousY = y;
        }

        return false;
    }

    private readonly record struct RenderedRow(DateTime SeenAt, string Text);
}
