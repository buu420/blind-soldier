namespace Ff7.Accessibility.Reloaded;

public sealed class FieldScriptNavigationTransitionTracker
{
    public static readonly TimeSpan DefaultGracePeriod = TimeSpan.FromSeconds(3);

    private readonly TimeSpan gracePeriod;
    private readonly Func<DateTime> utcNow;
    private readonly Dictionary<string, DateTime> lastEnabledAt = new(StringComparer.Ordinal);
    private int currentFieldId = -1;

    public FieldScriptNavigationTransitionTracker()
        : this(DefaultGracePeriod, () => DateTime.UtcNow)
    {
    }

    public FieldScriptNavigationTransitionTracker(TimeSpan gracePeriod, Func<DateTime> utcNow)
    {
        if (gracePeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(gracePeriod));
        }

        this.gracePeriod = gracePeriod;
        this.utcNow = utcNow;
    }

    public IReadOnlyList<FieldScriptNavigationTransition> Resolve(
        int fieldId,
        IReadOnlyList<FieldScriptNavigationTransition> transitions,
        Func<FieldScriptNavigationTransition, bool> isEnabled)
    {
        if (currentFieldId != fieldId)
        {
            currentFieldId = fieldId;
            lastEnabledAt.Clear();
        }

        if (transitions.Count == 0)
        {
            lastEnabledAt.Clear();
            return Array.Empty<FieldScriptNavigationTransition>();
        }

        var now = utcNow();
        var currentIds = new HashSet<string>(StringComparer.Ordinal);
        var available = new List<FieldScriptNavigationTransition>(transitions.Count);
        foreach (var transition in transitions)
        {
            if (transition.FieldId != fieldId || string.IsNullOrWhiteSpace(transition.StableId))
            {
                continue;
            }

            currentIds.Add(transition.StableId);
            if (isEnabled(transition))
            {
                lastEnabledAt[transition.StableId] = now;
                available.Add(transition);
                continue;
            }

            if (lastEnabledAt.TryGetValue(transition.StableId, out var observedAt) &&
                now - observedAt <= gracePeriod)
            {
                available.Add(transition);
            }
        }

        foreach (var stableId in lastEnabledAt.Keys.ToArray())
        {
            if (!currentIds.Contains(stableId) || now - lastEnabledAt[stableId] > gracePeriod)
            {
                lastEnabledAt.Remove(stableId);
            }
        }

        return available.Count == 0 ? Array.Empty<FieldScriptNavigationTransition>() : available;
    }
}
