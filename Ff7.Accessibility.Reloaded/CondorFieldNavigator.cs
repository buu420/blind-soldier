namespace Ff7.Accessibility.Reloaded;

/// <summary>What the player asked the battlefield navigator to do.</summary>
/// <remarks>
/// Deliberately the same shape as <see cref="FieldNavigationAction"/>, and bound
/// to the same keys, because a player who has learned U and O for categories and
/// J and L for targets on the field should not have to learn a second scheme for
/// the fort. It is a separate type because the fort's fifth action is a cursor
/// jump rather than a beacon, and because the field enum has no business growing
/// a member only module 9 uses.
/// </remarks>
public enum CondorNavigationAction
{
    PreviousCategory,
    NextCategory,
    PreviousTarget,
    NextTarget,
    JumpToTarget
}

/// <summary>The three lists the fort battlefield is worth walking.</summary>
public enum CondorNavigationCategory
{
    /// <summary>Units the player still has on the field.</summary>
    Allies,

    /// <summary>Live enemies, nearest the fort first.</summary>
    Enemies,

    /// <summary>Where the player's units fell, kept after the units are gone.</summary>
    Losses
}

/// <summary>One thing on the battlefield the player can select and jump to.</summary>
/// <param name="Key">
/// A stable identity, so a selection survives the list being rebuilt and re-sorted
/// under it. Live units use their slot, which is already unique across both sides;
/// losses use a negative counter so the two can never collide.
/// </param>
public readonly record struct CondorNavigationTarget(string Description, int X, int Y, int Key);

/// <summary>
/// Lets the player walk the fort battlefield as three lists - their own units,
/// the enemy, and where their losses fell - and put the cursor on any of them.
/// </summary>
/// <remarks>
/// <para>Everything here is spoken as a coordinate rather than as a direction and
/// a distance from the cursor. A direction stops being true the moment the cursor
/// moves, so it cannot be remembered; a coordinate is a fact about the
/// battlefield and can be. For the enemy it is also the better number outright:
/// the fort sits at low Y and the enemy advances toward it, so a falling Y is
/// exactly the progress the advance gauge is drawn from.</para>
///
/// <para>The losses list is the reason this exists rather than being a filter over
/// the live units. A unit that dies vanishes from the game's arrays, and the
/// ground it died on is often the ground the player most needs to think about -
/// it is where the enemy broke through. Nothing in the battle records that, so
/// this does.</para>
/// </remarks>
public sealed class CondorFieldNavigator
{
    private static readonly CondorNavigationCategory[] Categories =
    {
        CondorNavigationCategory.Allies,
        CondorNavigationCategory.Enemies,
        CondorNavigationCategory.Losses
    };

    private readonly List<CondorNavigationTarget> losses = new();
    private IReadOnlyList<CondorBattleUnit> units = Array.Empty<CondorBattleUnit>();
    private int categoryIndex;
    private int nextLossKey = -1;

    /// <summary>Where the cursor was at the last reading, for the locate readout.</summary>
    private int cursorX;
    private int cursorY;

    /// <summary>
    /// The selected thing's identity, not its position in the list.
    /// </summary>
    /// <remarks>
    /// The lists are rebuilt and re-sorted on every reading. Held as an index, a
    /// selection silently became a different unit whenever two of them crossed on
    /// the field, or whenever a new loss was pushed onto the front - so a player
    /// who had chosen their Fighter and pressed jump was moved somewhere else and
    /// told nothing.
    /// </remarks>
    private int? selectedKey;

    /// <summary>
    /// What the selection was called, so its disappearance can be announced rather
    /// than the player being moved silently onto whatever took its place.
    /// </summary>
    private string? selectedName;

    /// <summary>The category the player is currently walking.</summary>
    public CondorNavigationCategory Category => Categories[categoryIndex];

    /// <summary>The selected thing, if it is still on the current list.</summary>
    public CondorNavigationTarget? Current
    {
        get
        {
            if (selectedKey is not { } key)
            {
                return null;
            }

            foreach (var target in Build(Category))
            {
                if (target.Key == key)
                {
                    return target;
                }
            }

            return null;
        }
    }

    /// <summary>Takes the latest battlefield reading.</summary>
    public void Update(IReadOnlyList<CondorBattleUnit> liveUnits, int cursorX, int cursorY)
    {
        ArgumentNullException.ThrowIfNull(liveUnits);
        units = liveUnits;
        this.cursorX = cursorX;
        this.cursorY = cursorY;
    }

    /// <summary>
    /// Remembers where one of the player's units fell.
    /// </summary>
    /// <remarks>
    /// Called by the speech tracker, which already works out who has gone down
    /// between two readings; doing that diff twice would risk the two disagreeing
    /// about what died.
    /// </remarks>
    public void RecordLoss(CondorBattleUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);

        // Most recent first: the break-through that just happened is the one the
        // player needs. The key is what keeps an already-selected loss selected
        // while every index in front of it shifts.
        losses.Insert(0, new CondorNavigationTarget(
            $"{unit.Name}, lost", unit.X, unit.Y, nextLossKey--));
    }

    /// <summary>Clears everything a new battle should not inherit.</summary>
    public void Reset()
    {
        losses.Clear();
        units = Array.Empty<CondorBattleUnit>();
        categoryIndex = 0;
        nextLossKey = -1;
        selectedKey = null;
        selectedName = null;
    }

    /// <summary>
    /// Applies one navigation action and returns what to say, or null when the
    /// action was not one this navigator handles.
    /// </summary>
    /// <param name="moveCursor">
    /// Moves the game's own cursor, returning whether it took. Only
    /// <see cref="CondorNavigationAction.JumpToTarget"/> uses it.
    /// </param>
    public string? Handle(
        CondorNavigationAction action,
        Func<int, int, bool>? moveCursor = null)
    {
        switch (action)
        {
            case CondorNavigationAction.NextCategory:
                return ChangeCategory(1);
            case CondorNavigationAction.PreviousCategory:
                return ChangeCategory(-1);
            case CondorNavigationAction.NextTarget:
                return ChangeTarget(1);
            case CondorNavigationAction.PreviousTarget:
                return ChangeTarget(-1);
            case CondorNavigationAction.JumpToTarget:
                return Locate(moveCursor);
            default:
                return null;
        }
    }

    private string ChangeCategory(int step)
    {
        categoryIndex = Wrap(categoryIndex + step, Categories.Length);
        selectedKey = null;
        selectedName = null;

        var list = Build(Category);
        var name = Name(Category);
        if (list.Count == 0)
        {
            return $"{name}. None.";
        }

        // Land on the first entry rather than announcing a bare count: the player
        // asked to look at this list, so show them into it.
        Select(list[0]);
        return $"{name}. {list.Count}. {Describe(list[0])}";
    }

    private string ChangeTarget(int step)
    {
        var list = Build(Category);
        if (list.Count == 0)
        {
            selectedKey = null;
            selectedName = null;
            return $"{Name(Category)}. None.";
        }

        var current = IndexOfSelection(list);
        if (current < 0)
        {
            // Either nothing was selected yet, or what was selected has left the
            // field. Those are different events and only the second is worth a
            // word, but neither may move the player somewhere new in silence.
            var gone = selectedName;
            var landing = list[step > 0 ? 0 : list.Count - 1];
            Select(landing);
            return gone is null
                ? Describe(landing)
                : $"{Capitalize(gone)} gone. {Describe(landing)}";
        }

        var next = list[Wrap(current + step, list.Count)];
        Select(next);
        return Describe(next);
    }

    private int IndexOfSelection(IReadOnlyList<CondorNavigationTarget> list)
    {
        if (selectedKey is not { } key)
        {
            return -1;
        }

        for (var index = 0; index < list.Count; index++)
        {
            if (list[index].Key == key)
            {
                return index;
            }
        }

        return -1;
    }

    private void Select(CondorNavigationTarget target)
    {
        selectedKey = target.Key;
        selectedName = target.Description;
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>
    /// Sets the cursor going towards the selected thing if the host can steer.
    /// </summary>
    /// <remarks>
    /// <para>Moving the cursor is not a matter of storing a coordinate: the
    /// battle's cursor is camera-relative and the view has to travel with it,
    /// so a direct write leaves the player looking at ground they are not on -
    /// see <see cref="CondorCursorMover"/>. The cursor is steered instead by
    /// holding the game's own direction keys, which moves camera, accumulators
    /// and cursor together because the game does it.</para>
    ///
    /// <para>Steering takes time, so this announces only that movement began.
    /// The selected target was already named when J or L selected it, and
    /// repeating its coordinates here only lengthens a state announcement the
    /// player needs immediately. Where the cursor actually comes to rest is
    /// announced by the cursor readout after the keys are released, which is
    /// the truth whether or not the jump reached what it was aimed at. A host
    /// that cannot steer still says both positions and claims nothing.</para>
    /// </remarks>
    private string Locate(Func<int, int, bool>? moveCursor)
    {
        if (Current is not { } target)
        {
            return $"{Name(Category)}. None.";
        }

        if (moveCursor is not null && moveCursor(target.X, target.Y))
        {
            return "Moving.";
        }

        // Both coordinates, so the player can steer to it themselves and knows
        // exactly how far the cursor still has to travel.
        return $"{target.Description}, at {target.X}, {target.Y}. " +
            $"Cursor at {cursorX}, {cursorY}.";
    }

    private static string Describe(CondorNavigationTarget target) =>
        $"{target.Description}, at {target.X}, {target.Y}.";

    private static string Name(CondorNavigationCategory category) => category switch
    {
        CondorNavigationCategory.Allies => "Allies",
        CondorNavigationCategory.Enemies => "Enemies",
        _ => "Losses"
    };

    private static int Wrap(int index, int count) => ((index % count) + count) % count;

    private IReadOnlyList<CondorNavigationTarget> Build(CondorNavigationCategory category)
    {
        if (category == CondorNavigationCategory.Losses)
        {
            return losses;
        }

        var wantEnemies = category == CondorNavigationCategory.Enemies;
        return units
            // The same predicate the casualty diff uses. It was written out as
            // !IsRemoving && CurrentHp > 0, which is algebraically identical for
            // reader-produced snapshots, but two spellings of "alive" invite the
            // two to drift apart later.
            .Where(unit => unit.IsEnemy == wantEnemies && !unit.IsDying)
            // Ascending Y is nearest-the-fort first. The fort sits at low Y and the
            // enemy advances toward it, so for the enemy this is most-advanced
            // first. For the player's own units it is rearguard first, not the
            // front line - one rule for both lists, so there is only one thing to
            // learn, rather than an ordering that flips meaning between them.
            .OrderBy(unit => unit.Y)
            .ThenBy(unit => unit.X)
            .Select(unit => new CondorNavigationTarget(unit.Describe(), unit.X, unit.Y, unit.Slot))
            .ToList();
    }
}
