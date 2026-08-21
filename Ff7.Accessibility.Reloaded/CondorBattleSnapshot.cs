namespace Ff7.Accessibility.Reloaded;

/// <summary>One live unit on the Fort Condor battlefield.</summary>
/// <param name="Slot">Index into the live array. 0-19 are the player's, 20-39 the enemy's.</param>
/// <param name="TypeId">Native unit type, which indexes the <c>data.bin</c> record table.</param>
/// <param name="X">World X, in the same coordinate space as the cursor.</param>
/// <param name="Y">World Y, in the same coordinate space as the cursor.</param>
/// <param name="IsDying">Allocated but out of HP or already in its removal animation.</param>
public sealed record CondorBattleUnit(
    int Slot,
    bool IsEnemy,
    int TypeId,
    int CurrentHp,
    int MaximumHp,
    int Attack,
    int X,
    int Y,
    bool IsDying)
{
    /// <summary>
    /// What to call this unit out loud. Hireable types are named from the
    /// catalog; anything else is described by side alone rather than guessed at,
    /// because the enemy roster's type identifiers have not been tied to the
    /// names in <c>emes01.tex</c> yet.
    /// </summary>
    public string Name
    {
        get
        {
            var known = CondorUnitCatalog.ResolveByRecordIndex(TypeId)?.Name;
            if (known is null)
            {
                return IsEnemy ? "enemy unit" : "unit";
            }

            return IsEnemy ? $"enemy {known}" : known;
        }
    }

    public string Describe() => $"{Name}, {CurrentHp} of {MaximumHp}";
}

/// <summary>
/// Everything the Fort Condor battle shows a sighted player, read from memory.
/// </summary>
/// <remarks>
/// The battle draws its whole interface from textures, so none of this can be
/// intercepted as text. Every field here is read from a global the executable
/// itself writes for that purpose; the addresses and layouts are recorded in
/// <c>analysis/ghidra/fort-condor-live-battle-state-20260821.md</c>.
/// </remarks>
public sealed record CondorBattleSnapshot(
    int InteractionMode,
    int ModalState,
    int SettingMenuRow,
    int SettingMenuRotation,
    IReadOnlyList<int> AvailableTypeIds,
    int Gil,
    int CursorX,
    int CursorY,
    bool CursorPlacementLegal,
    int UnitUnderCursorSlot,
    IReadOnlyList<CondorBattleUnit> Units,
    int AlliedCount,
    int EnemyCount,
    int Outcome,
    int MessageId)
{
    public const int SettingMenuModalState = 7;

    /// <summary>Cursor mode: the player is moving the cursor over the battlefield.</summary>
    public const int CursorInteractionMode = 1;

    public bool SettingMenuOpen => ModalState == SettingMenuModalState;

    /// <summary>
    /// The unit type the Setting Menu is currently highlighting. The row is
    /// relative to a rotating window over the available list, so both have to be
    /// combined before the list can be indexed.
    /// </summary>
    public int? HighlightedTypeId
    {
        get
        {
            if (!SettingMenuOpen || AvailableTypeIds.Count == 0)
            {
                return null;
            }

            var index = (SettingMenuRow + SettingMenuRotation) % AvailableTypeIds.Count;
            if (index < 0)
            {
                index += AvailableTypeIds.Count;
            }

            return AvailableTypeIds[index];
        }
    }

    public CondorBattleUnit? UnitUnderCursor =>
        UnitUnderCursorSlot < 0
            ? null
            : Units.FirstOrDefault(unit => unit.Slot == UnitUnderCursorSlot);

    /// <summary>
    /// The nearest living enemy to the cursor, which is the thing a sighted
    /// player is judging placement against.
    /// </summary>
    public CondorBattleUnit? NearestEnemy => Units
        .Where(unit => unit is { IsEnemy: true, IsDying: false })
        .OrderBy(unit => DistanceFromCursorSquared(unit))
        .FirstOrDefault();

    public int DistanceFromCursor(CondorBattleUnit unit) =>
        (int)Math.Round(Math.Sqrt(DistanceFromCursorSquared(unit)));

    private long DistanceFromCursorSquared(CondorBattleUnit unit)
    {
        long dx = unit.X - CursorX;
        long dy = unit.Y - CursorY;
        return (dx * dx) + (dy * dy);
    }

    /// <summary>
    /// Where a unit lies relative to the cursor, said the way the rest of the mod
    /// says direction. Y grows downward on this battlefield.
    /// </summary>
    public string DirectionFromCursor(CondorBattleUnit unit)
    {
        var dx = unit.X - CursorX;
        var dy = unit.Y - CursorY;
        var vertical = dy switch
        {
            < -8 => "up",
            > 8 => "down",
            _ => string.Empty
        };
        var horizontal = dx switch
        {
            < -8 => "left",
            > 8 => "right",
            _ => string.Empty
        };

        if (vertical.Length == 0 && horizontal.Length == 0)
        {
            return "here";
        }

        return string.Join(" and ", new[] { vertical, horizontal }.Where(part => part.Length > 0));
    }
}
