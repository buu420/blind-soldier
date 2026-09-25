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
        FieldWalkmesh? walkmesh,
        Func<int, bool?>? isControlledEntity = null,
        Func<int?>? partyLeaderCharacter = null)
    {
        ArgumentNullException.ThrowIfNull(isLineEnabled);
        var available = Resolve(
            fieldId,
            transitions,
            transition => isLineEnabled(transition.SourceEntityId),
            isControlledEntity,
            partyLeaderCharacter);
        return walkmesh is null
            ? available
            : FieldWalkmeshRoutePlanner.AnchorTransitionSourceHeights(available, walkmesh);
    }

    /// <param name="isControlledEntity">
    /// Whether an entity's model is the one the player moves, or null when that cannot be
    /// read. A traversal that moves particular entities' models (its
    /// <see cref="FieldScriptNavigationTransition.MoverEntityIds"/>) is offered only while the
    /// reader positively says one of them is controlled: an unreadable or torn answer is not a
    /// verified route, so it hides the traversal until the next read. A traversal with no movers
    /// is the player's whoever is controlled and is always offered. Every runtime passes its live
    /// reader; leaving this out (offline listings and tests) filters nothing.
    /// </param>
    /// <param name="partyLeaderCharacter">
    /// The character in party slot 0 (0x00DC09E5), or null when it cannot be read. A traversal
    /// with <see cref="FieldScriptNavigationTransition.Conditions"/> is offered only while one of
    /// them holds as a whole - its model positively controlled and its leader positively leading,
    /// together: controlled Cloud does not stand in for leading Tifa. Left out, nothing is
    /// filtered on the leader.
    /// </param>
    public IReadOnlyList<FieldScriptNavigationTransition> Resolve(
        int fieldId,
        IReadOnlyList<FieldScriptNavigationTransition> transitions,
        Func<FieldScriptNavigationTransition, bool> isEnabled,
        Func<int, bool?>? isControlledEntity = null,
        Func<int?>? partyLeaderCharacter = null)
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
        // Read once, the first time a traversal needs it, and shared by every traversal.
        (bool Read, int? Character) leader = (false, null);
        var currentIds = new HashSet<string>(StringComparer.Ordinal);
        var available = new List<FieldScriptNavigationTransition>(transitions.Count);
        foreach (var transition in transitions)
        {
            if (transition.FieldId != fieldId || string.IsNullOrWhiteSpace(transition.StableId))
            {
                continue;
            }

            currentIds.Add(transition.StableId);

            // A traversal a character's own copy of a routine makes moves that character's
            // model, and is the player's only while that model is the one being moved:
            // Mount Corel's border9 lands Aeris somewhere other than everyone else, and
            // offering her landing while Cloud leads describes a place he is never sent. The
            // same holds for a ladder the field polls for, which moves whoever leads.
            if (!IsThePlayers(transition, isControlledEntity, partyLeaderCharacter, ref leader))
            {
                continue;
            }

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

    private static bool IsThePlayers(
        FieldScriptNavigationTransition transition,
        Func<int, bool?>? isControlledEntity,
        Func<int?>? partyLeaderCharacter,
        ref (bool Read, int? Character) leader)
    {
        if (transition.Conditions is not { Count: > 0 } conditions)
        {
            return MovesTheControlledModel(transition, isControlledEntity);
        }

        foreach (var condition in conditions)
        {
            if (condition.ControlledEntityId is { } entity && isControlledEntity is not null &&
                isControlledEntity(entity) != true)
            {
                continue;
            }

            if (condition.LeaderCharacterIds is { Count: > 0 } leaders && partyLeaderCharacter is not null)
            {
                if (!leader.Read)
                {
                    leader = (true, partyLeaderCharacter());
                }

                if (leader.Character is not { } character || !leaders.Contains(character))
                {
                    continue;
                }
            }

            return true;
        }

        return false;
    }

    private static bool MovesTheControlledModel(
        FieldScriptNavigationTransition transition,
        Func<int, bool?>? isControlledEntity)
    {
        if (transition.MoverEntityIds is not { Count: > 0 } movers || isControlledEntity is null)
        {
            return true;
        }

        foreach (var mover in movers)
        {
            if (isControlledEntity(mover) == true)
            {
                return true;
            }
        }

        return false;
    }
}
