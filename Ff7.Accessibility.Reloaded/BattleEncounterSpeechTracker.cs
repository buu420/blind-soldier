namespace Ff7.Accessibility.Reloaded;

public sealed class BattleEncounterSpeechTracker
{
    private readonly object sync = new();
    private bool announced;
    private string? pending;

    public void Observe(BattleEncounterSnapshot snapshot)
    {
        lock (sync)
        {
            if (announced || !snapshot.IsValid || snapshot.Enemies.Count == 0)
            {
                return;
            }

            var names = snapshot.Enemies
                .Select(enemy => enemy.Name.Trim())
                .Where(name => name.Length > 0)
                .ToArray();
            if (names.Length == 0)
            {
                return;
            }

            announced = true;
            pending = $"{DescribeLayout(snapshot.LayoutType)} Enemies: {JoinNames(names)}.";
        }
    }

    public string? Poll()
    {
        lock (sync)
        {
            var result = pending;
            pending = null;
            return result;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            announced = false;
            pending = null;
        }
    }

    private static string DescribeLayout(int layoutType) => layoutType switch
    {
        1 => "Preemptive attack.",
        2 => "Back attack.",
        3 or 6 or 7 => "Side attack.",
        4 or 5 => "Pincer attack.",
        8 => "Front-row battle.",
        _ => "Battle."
    };

    private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}"
    };
}
