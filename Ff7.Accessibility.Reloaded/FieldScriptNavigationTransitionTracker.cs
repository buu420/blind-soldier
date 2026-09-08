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

    /// <summary>
    /// The whole production step between the catalog and everything that navigates:
    /// which traversals are available right now, and at what height.
    /// </summary>
    /// <remarks>
    /// This exists as one method because the two halves have to happen in that order and
    /// nothing may skip either. Availability came first and dropped every traversal the
    /// field polls for rather than triggering from a LINE, so the height resolution that
    /// followed had nothing left to resolve - the Cosmo Canyon stairwell went from
    /// twenty-eight legs to none, and the shaft became seven islands again in the running
    /// game while every offline check still passed. Anything that navigates should call
    /// this rather than assembling the steps itself.
    /// </remarks>
    public IReadOnlyList<FieldScriptNavigationTransition> ResolveForNavigation(
        int fieldId,
        IReadOnlyList<FieldScriptNavigationTransition> transitions,
        Func<int, bool> isLineEnabled,
        FieldWalkmesh? walkmesh)
    {
        ArgumentNullException.ThrowIfNull(isLineEnabled);
        var available = Resolve(
            fieldId,
            transitions,
            transition => isLineEnabled(transition.SourceEntityId));
        return walkmesh is null
            ? available
            : FieldWalkmeshRoutePlanner.AnchorTransitionSourceHeights(available, walkmesh);
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

            // A traversal the field polls for has no LINE behind it, and asking whether
            // its line is switched on is asking about something that does not exist. The
            // field initializer leaves every entity's line mapping at 255 and only the
            // LINE opcode ever writes a real one, so the question comes back as the state
            // of line 255 - which is nothing to do with this ladder and, in the stairwell,
            // silently discarded all twenty-eight of its legs. What keeps a polled
            // traversal available is the loop that polls for it, and that is always
            // running while the party is in the field.
            if (transition.SourceTriangle >= 0)
            {
                available.Add(transition);
                continue;
            }

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
