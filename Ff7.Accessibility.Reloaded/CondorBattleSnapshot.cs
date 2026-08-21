namespace Ff7.Accessibility.Reloaded;

/// <summary>One live unit on the Fort Condor battlefield.</summary>
/// <param name="Slot">Index into the live array. 0-19 are the player's, 20-39 the enemy's.</param>
/// <param name="TypeId">Native unit type, which indexes the <c>data.bin</c> record table.</param>
/// <param name="X">World X, in the same coordinate space as the cursor.</param>
/// <param name="Y">World Y, in the same coordinate space as the cursor.</param>
/// <param name="IsDying">Allocated but out of HP or already in its removal animation.</param>
/// <param name="Width">Drawn width, which sets how much ground the unit denies to a new one.</param>
/// <param name="HeightAbove">How far above its own position the unit's footprint reaches.</param>
/// <param name="IsRemoving">
/// The removal byte at +0x05 is set. Kept apart from <paramref name="IsDying"/>
/// because the game's two scans disagree about these units on purpose: the
/// footprint scan still counts them, while the hit-box scan that decides which
/// unit the cursor is pointing at skips them.
/// </param>
public sealed record CondorBattleUnit(
    int Slot,
    bool IsEnemy,
    int TypeId,
    int CurrentHp,
    int MaximumHp,
    int Attack,
    int X,
    int Y,
    bool IsDying,
    int Width,
    int HeightAbove,
    bool IsRemoving = false)
{
    /// <summary>
    /// What to call this unit out loud. All 24 type identifiers the executable
    /// can draw are mapped to their exact <c>emes01.tex</c> cell; an out-of-range
    /// value is described by side alone rather than guessed at.
    /// </summary>
    public string Name
    {
        get
        {
            var known = CondorUnitCatalog.ResolveName(TypeId);
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
    int MessageId,
    int Phase,
    int ReportState,
    int DeploymentFrontierY,
    int EnemyAdvance,
    IReadOnlyList<CondorCollisionTriangle> CollisionTriangles)
{
    /// <summary>
    /// The value of <see cref="EnemyAdvance"/> when the enemy has reached the
    /// fort. The game derives the gauge from the leading enemy's position and
    /// draws it as a row of segments, so it is on screen throughout a battle.
    /// </summary>
    public const int EnemyAdvanceFull = 96;

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

    /// <summary>
    /// Every row of the cursor's current column that would accept a unit. The
    /// game only ever answers this for the exact spot the cursor is on, so the
    /// whole predicate is reproduced to answer it for the rest of the column.
    /// </summary>
    public IReadOnlyList<CondorPlacementInterval> PlacementIntervals =>
        CondorPlacementRegion.LegalIntervals(this);

    /// <summary>
    /// The units still fighting on each side, counted from the live array
    /// itself rather than from the separate counters, so a casualty and the
    /// number left standing after it can never come from two different readings.
    /// </summary>
    public int LivingAllies => Units.Count(unit => unit is { IsEnemy: false, IsDying: false });

    public int LivingEnemies => Units.Count(unit => unit is { IsEnemy: true, IsDying: false });

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
