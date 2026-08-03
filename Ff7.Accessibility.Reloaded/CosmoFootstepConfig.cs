namespace Ff7.Accessibility.Reloaded;

public sealed class CosmoFootstepConfig
{
    private readonly Dictionary<string, int[]> sequences;

    private CosmoFootstepConfig(Dictionary<string, int[]> sequences)
    {
        this.sequences = sequences;
    }

    public static CosmoFootstepConfig Empty { get; } = new(new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase));

    public int TrackCount => sequences.Count;

    public static CosmoFootstepConfig Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : Empty;

    public static CosmoFootstepConfig Parse(string text)
    {
        var parsedSequences = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);
        string? currentSection = null;

        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } rawLine)
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            if (currentSection is null || !line.StartsWith("sequential", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var equalsIndex = line.IndexOf('=');
            var openIndex = line.IndexOf('[', equalsIndex + 1);
            var closeIndex = line.LastIndexOf(']');
            if (equalsIndex < 0 || openIndex < 0 || closeIndex <= openIndex)
            {
                continue;
            }

            var ids = ParseIds(line[(openIndex + 1)..closeIndex]);
            if (ids is not null)
            {
                parsedSequences[currentSection] = ids;
            }
        }

        return new CosmoFootstepConfig(parsedSequences);
    }

    public bool TryGetSequence(string trackName, out IReadOnlyList<int> sequence)
    {
        if (sequences.TryGetValue(trackName, out var ids))
        {
            sequence = ids;
            return true;
        }

        sequence = Array.Empty<int>();
        return false;
    }

    private static int[]? ParseIds(string text)
    {
        var ids = new List<int>();
        var sawNumber = false;
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var id))
            {
                continue;
            }

            sawNumber = true;
            if (id > 0)
            {
                ids.Add(id);
            }
        }

        return sawNumber ? ids.ToArray() : null;
    }

    private static string StripComment(string line)
    {
        var index = line.IndexOf('#');
        return index >= 0 ? line[..index] : line;
    }
}
