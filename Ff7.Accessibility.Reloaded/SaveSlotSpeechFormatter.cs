namespace Ff7.Accessibility.Reloaded;

public static class SaveSlotSpeechFormatter
{
    public static string? FormatGame(int gameNumber, Ff7SaveSlotPreview? preview)
    {
        if (preview is null)
        {
            return null;
        }

        if (preview is not { IsEmpty: false } value)
        {
            return $"Game {gameNumber}. Empty.";
        }

        return $"Game {gameNumber}. {value.LeadCharacterName}, level {value.Level}. " +
            $"{value.Location.Trim().TrimEnd('.')}. HP {value.CurrentHp} of {value.MaxHp}. " +
            $"MP {value.CurrentMp} of {value.MaxMp}. Time {FormatTime(value.PlaySeconds)}. {value.Gil} gil.";
    }

    private static string FormatTime(uint totalSeconds)
    {
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        var parts = new List<string>(3);
        if (hours > 0)
        {
            parts.Add($"{hours} {(hours == 1 ? "hour" : "hours")}");
        }

        if (minutes > 0 || hours > 0)
        {
            parts.Add($"{minutes} {(minutes == 1 ? "minute" : "minutes")}");
        }

        parts.Add($"{seconds} {(seconds == 1 ? "second" : "seconds")}");
        return string.Join(", ", parts);
    }
}
