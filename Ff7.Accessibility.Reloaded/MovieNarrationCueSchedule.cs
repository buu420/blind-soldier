using System.Globalization;
using System.Text.RegularExpressions;

namespace Ff7.Accessibility.Reloaded;

/// <summary>One described moment inside a film, at the time it belongs to.</summary>
/// <param name="Start">Seconds from the start of the film.</param>
/// <param name="End">Seconds from the start of the film; the recording's own end.</param>
public readonly record struct MovieNarrationCue(double Start, double End, string Text);

/// <summary>
/// The cue windows a recording is built from, read from the sidecar that ships beside
/// it.
///
/// <para>This exists so a film with the game's own words over it can still be
/// described. The recording is one continuous track and the only controls it has are
/// start and stop, so it cannot duck: when a dialogue window opens there is no way to
/// hold the audio without it running behind the picture. The cues let the rest of the
/// film be described anyway - each remaining moment is spoken at the time it belongs
/// to, through the ordinary speech path, which already yields to dialogue and is
/// already what the player hears when a recording is missing.</para>
///
/// <para>The sidecar is the one root's tooling writes and the packaging test already
/// checks: a JSON array of <c>description</c>, <c>start_time</c> and <c>end_time</c>,
/// the times as <c>HH:MM:SS.mmm</c>.</para>
/// </summary>
public static class MovieNarrationCueSchedule
{
    private static readonly Regex Entry = new(
        "\\{[^{}]*\\}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Field = new(
        "\"(?<name>description|start_time|end_time)\"\\s*:\\s*\"(?<value>(?:[^\"\\\\]|\\\\.)*)\"",
        RegexOptions.Compiled);

    /// <summary>
    /// Reads a sidecar. Anything malformed yields an empty schedule rather than a
    /// throw: a film with no readable cues falls back to the ordinary paragraph,
    /// which is the same thing that happens when the recording itself is missing.
    /// </summary>
    public static IReadOnlyList<MovieNarrationCue> Parse(string? sidecar)
    {
        if (string.IsNullOrWhiteSpace(sidecar))
        {
            return [];
        }

        var cues = new List<MovieNarrationCue>();
        foreach (Match entry in Entry.Matches(sidecar))
        {
            string? text = null;
            double? start = null;
            double? end = null;
            foreach (Match field in Field.Matches(entry.Value))
            {
                var value = field.Groups["value"].Value;
                switch (field.Groups["name"].Value)
                {
                    case "description":
                        text = value.Replace("\\\"", "\"").Replace("\\n", " ").Trim();
                        break;
                    case "start_time":
                        start = ParseTimestamp(value);
                        break;
                    case "end_time":
                        end = ParseTimestamp(value);
                        break;
                }
            }

            if (text is not { Length: > 0 } || start is not { } from || end is not { } to || to < from)
            {
                continue;
            }

            cues.Add(new MovieNarrationCue(from, to, text));
        }

        cues.Sort((left, right) => left.Start.CompareTo(right.Start));
        return cues;
    }

    private static double? ParseTimestamp(string value)
    {
        var parts = value.Split(':');
        if (parts.Length != 3)
        {
            return null;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
            !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        return (hours * 3600d) + (minutes * 60d) + seconds;
    }
}
