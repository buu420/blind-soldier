namespace Ff7.Accessibility.Core;

/// <summary>
/// Selects the newly visible suffix when a native text page extends an
/// already delivered page at a word boundary.
/// </summary>
public static class VisibleTextContinuation
{
    public static string SelectDeliveryText(string? previouslyDeliveredText, string currentText)
    {
        ArgumentNullException.ThrowIfNull(currentText);
        if (string.IsNullOrEmpty(previouslyDeliveredText)
            || currentText.Length <= previouslyDeliveredText.Length
            || !currentText.StartsWith(previouslyDeliveredText, StringComparison.Ordinal))
        {
            return currentText;
        }

        var boundary = currentText[previouslyDeliveredText.Length];
        if (!char.IsWhiteSpace(boundary))
        {
            return currentText;
        }

        var suffix = currentText[previouslyDeliveredText.Length..].Trim();
        return suffix.Length == 0 ? currentText : suffix;
    }
}
