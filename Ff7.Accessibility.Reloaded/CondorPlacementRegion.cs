namespace Ff7.Accessibility.Reloaded;

/// <summary>A run of cursor rows, inclusive at both ends, where a unit can be placed.</summary>
public readonly record struct CondorPlacementInterval(int FromY, int ToY)
{
    public bool Contains(int y) => y >= FromY && y <= ToY;
}

/// <summary>
/// One collision triangle from <c>vert.bin</c>, in the coordinate space the
/// placement test uses.
/// </summary>
public readonly record struct CondorCollisionTriangle(
    int Ax, int Ay, int Bx, int By, int Cx, int Cy,
    int MinX, int MaxX, int MinY, int MaxY)
{
    /// <summary>
    /// The record's own inclusive bounds, biased by 0x4000 in the file. The game
    /// applies them before the triangle test, and reproducing that keeps the
    /// per-row cost down as well as matching what the game does.
    /// </summary>
    public bool WithinBounds(int x, int y) =>
        x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    /// <summary>
    /// Whether the point lies in the triangle, edges included.
    ///
    /// <para>The game decides this with fixed-point wedge angles and an
    /// eight-unit tolerance out of a 4096-unit turn. This is the ordinary
    /// integer cross-product test instead, which is exact and has no tolerance
    /// of its own. The four cursor columns published from the disassembly come
    /// out identical, edges and all, which is the regression anchor pinned by
    /// the test suite; it is not an exhaustive proof over the whole mesh.</para>
    /// </summary>
    public bool Contains(int x, int y)
    {
        var ab = ((long)(x - Bx) * (Ay - By)) - ((long)(Ax - Bx) * (y - By));
        var bc = ((long)(x - Cx) * (By - Cy)) - ((long)(Bx - Cx) * (y - Cy));
        var ca = ((long)(x - Ax) * (Cy - Ay)) - ((long)(Cx - Ax) * (y - Ay));

        var negative = ab < 0 || bc < 0 || ca < 0;
        var positive = ab > 0 || bc > 0 || ca > 0;
        return !(negative && positive);
    }
}

/// <summary>
/// Works out where a unit can actually be placed, without moving the cursor.
/// </summary>
/// <remarks>
/// A sighted player looks at the hill and sees where the ground will take a
/// unit. The mod cannot ask the game the same question about anywhere except
/// the exact spot the cursor is on, and the flag that answers even that is
/// rewritten several times per frame, so it is unsafe to sample. Everything the
/// validator uses is readable, so the whole predicate is reproduced here
/// instead and evaluated at every row of the column the cursor is in.
///
/// <para>The result is a list of intervals, never a single pair. The region
/// genuinely has holes: at cursor X 260 the ground is placeable from 420 to 476
/// and again from 552 down, and calling that "420 to 1008" would describe a
/// seventy-row gap as buildable. Live units cut further holes out of what is
/// left.</para>
///
/// <para>Established in <c>analysis/ghidra/fort-condor-placement-region-20260821.md</c>
/// from the executable's own validator.</para>
/// </remarks>
public static class CondorPlacementRegion
{
    /// <summary>Battle phase in which the fixed setup boundary applies.</summary>
    public const int SetupPhase = 1;

    /// <summary>
    /// The setup boundary, compared inclusively. The cursor moves in four-unit
    /// steps from zero, so the last row a player can actually reach under it
    /// is 668.
    /// </summary>
    public const int SetupBoundaryY = 671;

    public const int CursorStep = 4;
    public const int MaximumCursorY = 1008;

    /// <summary>Placement is refused once the player already has this many units.</summary>
    public const int MaximumAlliedUnits = 20;

    /// <summary>The collision records are indexed from a different origin than the cursor.</summary>
    public const int CollisionOriginX = 256;
    public const int CollisionOriginY = 512;

    /// <summary>
    /// The native overlap scan runs slots 0 to 38. Slot 39 is genuinely not
    /// examined - the loop bound is 0x27 and the comparison is exclusive - so a
    /// unit there does not block placement in the game either.
    /// </summary>
    public const int OverlapScanSlotLimit = 39;

    /// <summary>
    /// Every row of the current column where a unit could be placed, in order.
    /// Empty when nothing in this column can take one.
    /// </summary>
    public static IReadOnlyList<CondorPlacementInterval> LegalIntervals(CondorBattleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!CanPlaceAtAll(snapshot))
        {
            return [];
        }

        var intervals = new List<CondorPlacementInterval>();
        var runStart = -1;
        var runEnd = -1;

        // Anchored to the row the cursor is actually on. During setup the cursor
        // sits on multiples of the step, but in combat it does not - it was
        // observed at 525, 761 and 937 - and scanning from zero would then skip
        // the player's own row and report distances off by up to three.
        var first = ((snapshot.CursorY % CursorStep) + CursorStep) % CursorStep;

        for (var y = first; y <= MaximumCursorY; y += CursorStep)
        {
            if (IsLegalAt(snapshot, snapshot.CursorX, y))
            {
                if (runStart < 0)
                {
                    runStart = y;
                }

                runEnd = y;
                continue;
            }

            if (runStart >= 0)
            {
                intervals.Add(new CondorPlacementInterval(runStart, runEnd));
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            intervals.Add(new CondorPlacementInterval(runStart, runEnd));
        }

        return intervals;
    }

    /// <summary>
    /// Whether one position would be accepted. This is the validator's predicate,
    /// with the position supplied rather than taken from the live cursor, so it
    /// can answer for rows the player is not standing on.
    /// </summary>
    public static bool IsLegalAt(CondorBattleSnapshot snapshot, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!CanPlaceAtAll(snapshot) || y > VerticalLimit(snapshot))
        {
            return false;
        }

        foreach (var unit in snapshot.Units)
        {
            if (IsDirectlyUnder(unit, x, y) || Overlaps(unit, x, y))
            {
                return false;
            }
        }

        return IsOnTerrain(snapshot, x, y);
    }

    /// <summary>
    /// The lowest row placement is allowed on, whatever the terrain says.
    ///
    /// <para>During setup this is a fixed line in the executable. Once the battle
    /// is running it becomes a frontier that starts at 480 and moves down as the
    /// player's own units advance, to a limit of 928 - so the area a player can
    /// build in genuinely grows during a battle, and is worth telling them
    /// about.</para>
    /// </summary>
    public static int VerticalLimit(CondorBattleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Setup compares inclusively; combat compares strictly, so one less.
        return snapshot.Phase == SetupPhase
            ? SetupBoundaryY
            : snapshot.DeploymentFrontierY - 1;
    }

    /// <summary>The gates that have nothing to do with where the cursor is.</summary>
    private static bool CanPlaceAtAll(CondorBattleSnapshot snapshot) =>
        snapshot.ModalState == 0 &&
        snapshot.ReportState == 0 &&
        snapshot.AlliedCount < MaximumAlliedUnits &&
        snapshot.CollisionTriangles.Count > 0;

    private static bool IsOnTerrain(CondorBattleSnapshot snapshot, int x, int y)
    {
        var collisionX = x - CollisionOriginX;
        var collisionY = y - CollisionOriginY;

        foreach (var triangle in snapshot.CollisionTriangles)
        {
            if (triangle.WithinBounds(collisionX, collisionY) &&
                triangle.Contains(collisionX, collisionY))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The hit box that decides which unit the cursor is treated as pointing at.
    /// Asymmetric in Y, and the game's own bounds are exclusive.
    /// </summary>
    /// <remarks>
    /// A unit playing its removal animation is skipped, because the game's own
    /// scan skips it. The footprint test below deliberately does the opposite
    /// and still counts these units, which is also what the game does. Because
    /// that wider footprint covers slots 0 through 38, the removal distinction
    /// changes the final placement answer only for slot 39.
    /// </remarks>
    private static bool IsDirectlyUnder(CondorBattleUnit unit, int x, int y) =>
        !unit.IsRemoving &&
        unit.X > x - 13 && unit.X < x + 13 &&
        unit.Y > y - 10 && unit.Y < y + 14;

    /// <summary>
    /// The footprint an existing unit denies to a new one. Wider than the hit box
    /// above, taken from the unit's own drawn extents, and inclusive at every
    /// edge. A unit that is dying still blocks the ground until its slot is
    /// released.
    /// </summary>
    private static bool Overlaps(CondorBattleUnit unit, int x, int y)
    {
        if (unit.Slot >= OverlapScanSlotLimit)
        {
            return false;
        }

        var halfWidth = (unit.Width + 28) >> 1;
        return x >= unit.X - halfWidth && x <= unit.X + halfWidth &&
               y >= unit.Y - unit.HeightAbove && y <= unit.Y + 22;
    }

    /// <summary>
    /// Says a list of intervals the way a person would say it, in rows relative
    /// to where the cursor is now, because an absolute row number means nothing
    /// to someone who cannot see the hill.
    /// </summary>
    /// <summary>
    /// Says where a unit can be put in the cursor's column, in coordinates.
    /// </summary>
    /// <remarks>
    /// Coordinates rather than a direction and a distance, throughout. A relative
    /// answer is only true until the cursor moves, so it cannot be carried; the
    /// band 420 to 476 is a fact about the hill that stays true all battle and can
    /// be remembered, returned to, and jumped into. The player asked for this
    /// explicitly and it is the same rule the battlefield navigator follows.
    /// </remarks>
    public static string Describe(
        IReadOnlyList<CondorPlacementInterval> intervals, int cursorX, int cursorY)
    {
        ArgumentNullException.ThrowIfNull(intervals);

        if (intervals.Count == 0)
        {
            return "nowhere in this column";
        }

        var containing = intervals
            .Select(interval => (CondorPlacementInterval?)interval)
            .FirstOrDefault(interval => interval!.Value.Contains(cursorY));
        if (containing is { } here)
        {
            var band = here.FromY == here.ToY
                ? $"one row only at {here.FromY}"
                : $"{here.FromY} to {here.ToY}";

            return intervals.Count == 1
                ? $"placeable {band}"
                : $"placeable {band}, {intervals.Count - 1} more {(intervals.Count == 2 ? "band" : "bands")}";
        }

        var nearest = intervals
            .OrderBy(interval => interval.Contains(cursorY)
                ? 0
                : Math.Min(Math.Abs(interval.FromY - cursorY), Math.Abs(interval.ToY - cursorY)))
            .First();

        // The edge of that band the cursor would reach first, given as a place
        // rather than as a number of rows to travel.
        var edgeY = nearest.ToY < cursorY ? nearest.ToY : nearest.FromY;
        return $"blocked, nearest placeable at {cursorX}, {edgeY}";
    }
}
