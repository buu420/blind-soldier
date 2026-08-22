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
public readonly record struct CondorNavigationTarget(string Description, int X, int Y);

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
    private int targetIndex = -1;

    /// <summary>The category the player is currently walking.</summary>
    public CondorNavigationCategory Category => Categories[categoryIndex];

    /// <summary>The selected thing, if the current list has one.</summary>
    public CondorNavigationTarget? Current
    {
        get
        {
            var list = Build(Category);
            return list.Count == 0 || targetIndex < 0 || targetIndex >= list.Count
                ? null
                : list[targetIndex];
        }
    }

    /// <summary>Takes the latest battlefield reading.</summary>
    public void Update(IReadOnlyList<CondorBattleUnit> liveUnits, int cursorX, int cursorY)
    {
        ArgumentNullException.ThrowIfNull(liveUnits);
        units = liveUnits;
        _ = cursorX;
        _ = cursorY;
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
        // player needs, and a stable order matters less than reaching it quickly.
        losses.Insert(0, new CondorNavigationTarget(
            $"{unit.Name}, lost", unit.X, unit.Y));
    }

    /// <summary>Clears everything a new battle should not inherit.</summary>
    public void Reset()
    {
        losses.Clear();
        units = Array.Empty<CondorBattleUnit>();
        categoryIndex = 0;
        targetIndex = -1;
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
                return Jump(moveCursor);
            default:
                return null;
        }
    }

    private string ChangeCategory(int step)
    {
        categoryIndex = Wrap(categoryIndex + step, Categories.Length);
        targetIndex = -1;

        var list = Build(Category);
        var name = Name(Category);
        if (list.Count == 0)
        {
            return $"{name}. None.";
        }

        // Land on the first entry rather than announcing a bare count: the player
        // asked to look at this list, so show them into it.
        targetIndex = 0;
        return $"{name}. {list.Count}. {Describe(list[0])}";
    }

    private string ChangeTarget(int step)
    {
        var list = Build(Category);
        if (list.Count == 0)
        {
            return $"{Name(Category)}. None.";
        }

        targetIndex = targetIndex < 0
            ? (step > 0 ? 0 : list.Count - 1)
            : Wrap(targetIndex + step, list.Count);
        return Describe(list[targetIndex]);
    }

    private string Jump(Func<int, int, bool>? moveCursor)
    {
        if (Current is not { } target)
        {
            return $"{Name(Category)}. None.";
        }

        // Saying nothing, or saying it moved when it did not, would leave the
        // player reading the wrong ground and trusting it.
        if (moveCursor is null || !moveCursor(target.X, target.Y))
        {
            return $"Could not move the cursor to {target.X}, {target.Y}.";
        }

        return $"Cursor at {target.X}, {target.Y}. {target.Description}.";
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
            .Where(unit => unit.IsEnemy == wantEnemies && !unit.IsRemoving && unit.CurrentHp > 0)
            // Ascending Y is nearest-the-fort first, which for the enemy is most
            // advanced first and for the player's own units is the front line.
            .OrderBy(unit => unit.Y)
            .ThenBy(unit => unit.X)
            .Select(unit => new CondorNavigationTarget(unit.Describe(), unit.X, unit.Y))
            .ToList();
    }
}
