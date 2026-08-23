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

/// <summary>The Ally Unit command rows drawn from <c>eunit01.tex</c>.</summary>
public sealed record CondorAllyUnitMenu(
    int HighlightedRow,
    IReadOnlyList<int> CommandIds)
{
    public int? HighlightedCommandId =>
        HighlightedRow >= 0 && HighlightedRow < CommandIds.Count
            ? CommandIds[HighlightedRow]
            : null;
}

/// <summary>The live units offered by the selector when their hit boxes overlap.</summary>
public sealed record CondorCrowdedUnitMenu(
    int HighlightedRow,
    IReadOnlyList<int> UnitSlots)
{
    public int? HighlightedUnitSlot =>
        HighlightedRow >= 0 && HighlightedRow < UnitSlots.Count
            ? UnitSlots[HighlightedRow]
            : null;
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
    IReadOnlyList<CondorCollisionTriangle> CollisionTriangles,
    CondorAllyUnitMenu? AllyUnitMenu = null,
    int StartGameSelection = 0,
    CondorCrowdedUnitMenu? CrowdedUnitMenu = null,
    int DirectionSelection = 0,
    int ReportMessageCell = -1,
    int ReportUnitSlot = -1)
{
    /// <summary>
    /// The command destination cursor used by interaction mode 3. Module 9
    /// keeps it separately from the ordinary battlefield cursor.
    /// </summary>
    public int DestinationX { get; init; }

    /// <inheritdoc cref="DestinationX"/>
    public int DestinationY { get; init; }

    /// <summary>
    /// The four-level battle-speed gauge drawn on the battlefield. Page Up and
    /// Page Down change this value directly.
    /// </summary>
    public int GameSpeed { get; init; } = 2;

    /// <summary>
    /// The direction bits module 9 currently believes are held, straight out of
    /// its own input mask.
    /// </summary>
    /// <remarks>
    /// The battle does not read the keyboard the way the rest of the mod does:
    /// it polls DirectInput's immediate state, applies the player's own
    /// ff7input.cfg mapping, and only then sets these bits. They are therefore
    /// the only trustworthy evidence that a synthesized keystroke arrived - a
    /// key Windows accepted can still mean nothing to the battle.
    /// </remarks>
    public uint HeldDirectionMask { get; init; }

    /// <summary>
    /// Whether the direction keys are moving the battlefield cursor right now.
    /// </summary>
    /// <remarks>
    /// Cursor mode is not the only thing that has to be true. A modal overlay
    /// or a report gives the same keys to something else, and steering through
    /// one would be operating a menu the player did not ask to open.
    /// </remarks>
    public bool CursorUnderPlayerControl =>
        InteractionMode == CursorInteractionMode &&
        ModalState == 0 &&
        ReportState == 0;

    /// <summary>
    /// The value of <see cref="EnemyAdvance"/> when the enemy has reached the
    /// fort. The game derives the gauge from the leading enemy's position and
    /// draws it as a row of segments, so it is on screen throughout a battle.
    /// </summary>
    public const int EnemyAdvanceFull = 96;

    public const int SettingMenuModalState = 7;
    public const int NewUnitDirectionModalState = 8;
    public const int PauseModalState = 9;
    public const int StartGameModalState = 10;
    public const int HelpModalState = 14;
    public const int CrowdedUnitModalState = 15;
    public const int CommandDirectionModalState = 16;

    /// <summary>Cursor mode: the player is moving the cursor over the battlefield.</summary>
    public const int CursorInteractionMode = 1;
    public const int AllyUnitInteractionMode = 2;
    public const int DestinationInteractionMode = 3;

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

    public CondorBattleUnit? HighlightedCrowdedUnit =>
        CrowdedUnitMenu?.HighlightedUnitSlot is { } slot
            ? Units.FirstOrDefault(unit => unit.Slot == slot)
            : null;

    public CondorBattleUnit? ReportingUnit =>
        ReportUnitSlot < 0
            ? null
            : Units.FirstOrDefault(unit => unit.Slot == ReportUnitSlot);

    /// <summary>
    /// One-based position in the 33-step direction selector shared by modals 8
    /// and 16. The native control ranges from zero through 0x400 in 0x20 steps.
    /// </summary>
    public int DirectionOrdinal => (DirectionSelection / 0x20) + 1;

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
