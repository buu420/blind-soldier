namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Where the Fort Condor battle line is, and where the cursor stands relative to
/// it.
/// </summary>
/// <remarks>
/// <para>The line is the whole minigame. During setup it is a fixed wall in the
/// executable at <see cref="CondorPlacementRegion.SetupBoundaryY"/>. Once the
/// battle is running it becomes a frontier that follows the player's own units
/// down the mountain, so the ground they are allowed to build on grows as they
/// advance - which is what every walkthrough's "place a unit, start, then keep
/// placing further down" tip is exploiting.</para>
///
/// <para>A sighted player sees that line move. Brice could not, and on
/// 2026-08-22 he fought a whole battle placing every unit on the setup wall at
/// 671 - including units bought after the battle had started, when the window
/// below it had already opened. He lost. Nothing in the mod ever said the line
/// had moved. This is that fact, on a key.</para>
///
/// <para>On a key rather than in the running readout on purpose: the earlier
/// version of this was spoken with every placement answer and buried the
/// coordinates the cursor readout exists to deliver, which is why it was taken
/// out on 2026-08-22. It is reference the player asks for, not commentary.</para>
/// </remarks>
public static class CondorPlacementLineReadout
{
    /// <summary>
    /// Says where the line is and how far the cursor is from it.
    /// </summary>
    public static string Describe(CondorBattleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var line = CondorPlacementRegion.VerticalLimit(snapshot);
        var parts = new List<string>
        {
            $"Battle line at {line}",
            DescribeCursor(snapshot.CursorY, line)
        };

        // Only once it has actually moved. Saying "advanced 0" every time during
        // setup would be noise, and the point of the sentence is to tell the
        // player the mechanic has started working for them.
        var advanced = line - CondorPlacementRegion.SetupBoundaryY;
        if (advanced > 0)
        {
            parts.Add($"Advanced {advanced} since setup");
        }

        return string.Join(". ", parts) + ".";
    }

    private static string DescribeCursor(int cursorY, int line)
    {
        // Higher Y is further down the mountain, towards the enemy. "Below the
        // line" therefore means past it - ground the game will not let a unit be
        // placed on - and that is worth saying in those words rather than as a
        // signed number the player has to interpret.
        var distance = line - cursorY;
        // Each part becomes its own sentence when the caller joins them, so each
        // one opens in upper case.
        return distance switch
        {
            0 => $"Cursor at {cursorY}, on the line",
            > 0 => $"Cursor at {cursorY}, {distance} short of it",
            _ => $"Cursor at {cursorY}, {-distance} past it"
        };
    }
}
