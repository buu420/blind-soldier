namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// A native walkmesh lock, and the line the player has to walk onto to release it.
/// </summary>
/// <param name="FieldId">The field the lock belongs to.</param>
/// <param name="LockedTriangle">The triangle the field's own script holds shut.</param>
/// <param name="LineEntityId">
/// The entity that declares the line. The game enables and disables lines through that
/// entity with <c>LINON</c>, so this is what the live state has to be read for: both of
/// these lines are switched off by their own scripts at some point, and walking to one that
/// is off opens nothing.
/// </param>
/// <param name="Line">The <c>LINE</c> the releasing script is attached to.</param>
/// <param name="Evidence">The native instructions this row was read from.</param>
public readonly record struct FieldNativeDoorOpeningLine(
    int FieldId,
    int LockedTriangle,
    int LineEntityId,
    FieldNavigationTriggerLine Line,
    string Evidence)
{
    /// <summary>Where to stand to cross the line: its far endpoint.</summary>
    public int ApproachX => Line.EndX;

    /// <summary>Where to stand to cross the line: its far endpoint.</summary>
    public int ApproachY => Line.EndY;

    /// <summary>Where to stand to cross the line: its far endpoint.</summary>
    public int ApproachZ => Line.EndZ;
}

/// <summary>
/// Locks that are released by walking somewhere, rather than by waiting.
///
/// <para>Most native walkmesh locks open on the game's own schedule: a scene finishes and
/// the script releases the triangle. Holding a destination and starting it when the lock
/// clears is the right answer for those. Cosmo Canyon's two observatory doors are not like
/// that. Each is locked by its field's own init and released by a <c>LINE</c> script that
/// only runs when the party crosses it, so a held destination there waits forever - which
/// is exactly what the report described.</para>
///
/// <para>Every row is read from the installed field scripts and is identical in both
/// archives. Nothing here bypasses a lock: the line is somewhere the player can already
/// walk with the door still shut, and crossing it is what the game itself treats as the
/// request to open the door.</para>
///
/// <para>What crossing means is the game's own test, not an arrival tolerance. The
/// native line step squares the entity record's radius at <c>+0x72</c> and compares it
/// with the squared point-to-segment distance; <c>radius * radius &lt;= distanceSquared</c>
/// marks the player outside it, so activation requires <c>distanceSquared &lt; radius²</c>.
/// Stopping near a line is not standing on it, and the replay tests measure it that way
/// rather than trusting anything here.</para>
/// </summary>
public static class FieldNativeDoorOpeningLines
{
    private static readonly FieldNativeDoorOpeningLine[] Known =
    [
        // 541 bugin1a. Entity 15 LINEW Init declares the line and locks triangle 1, and
        // leaves it enabled - there is no LINON in Init at all. Its Go 1x is unconditional:
        // LINON 0, IDLCK 1 unlock, the door sound, then the Director's door animation.
        // Scripts 7 and 8 are LINON 0 and LINON 1, so the game does switch this line off
        // and on elsewhere and the live state still has to be read rather than assumed.
        new(541, 1, 15, new FieldNavigationTriggerLine(-321, -3, -35, -312, 64, -35),
            "e15 LINEW Init LINE + IDLCK 1 lock; Go 1x LINON 0 + IDLCK 1 unlock; s7 LINON 0, s8 LINON 1"),

        // 544 bugin2. Entity 18 DOOR Init locks triangle 38. Entity 19 LINEW Init declares
        // the line and then decides whether to leave it on: it reaches the LINON 0 at byte
        // 27 only while Bank[3][161] bit 3 is set and Bank[3][170] bit 0 is not - that is,
        // exactly while the observatory scene is still pending - so during that window the
        // line is dead and walking to it opens nothing. AD 10 Script 3 sets bit 0 and then
        // runs this entity's Script 8, which is LINON 1. Go 1x unlocks 38 on both of its
        // branches; before GameMoment 493 the second branch also wants Bank[5][15] to be 1,
        // so the walk is a request to open the door and never a promise that it did.
        new(544, 38, 19, new FieldNavigationTriggerLine(186, -117, -624, 118, -174, -624),
            "e18 DOOR Init IDLCK 38 lock; e19 LINEW Init LINE + gated LINON 0, Go 1x IDLCK 38 unlock, s8 LINON 1"),
    ];

    /// <summary>
    /// The line that releases this lock, if walking onto one is what releases it.
    ///
    /// <para>A lock with no row here is one the game opens by itself - Red XIII's own
    /// triangles 23 and 18 in 544 are released by his script 10 when he leaves, and
    /// cos_top's triangle 76 is never released at all - and those keep the existing
    /// behaviour of being held silently until they clear.</para>
    /// </summary>
    public static bool TryFind(int fieldId, int lockedTriangle, out FieldNativeDoorOpeningLine opening)
    {
        foreach (var candidate in Known)
        {
            if (candidate.FieldId == fieldId && candidate.LockedTriangle == lockedTriangle)
            {
                opening = candidate;
                return true;
            }
        }

        opening = default;
        return false;
    }

    /// <summary>
    /// The opening line for the lock that is actually in this route's way.
    ///
    /// <para>The boundary reports <em>every</em> triangle the field currently holds shut,
    /// not the ones on the path to the destination being asked for. Picking the first row
    /// that matches any of them would send a party heading for one place to the door of
    /// another. <paramref name="releasingThisWouldRoute"/> is asked, per candidate, whether
    /// releasing that one triangle is what lets this route through; only then is its line
    /// the right one to walk to.</para>
    /// </summary>
    public static bool TryFindForGoal(
        int fieldId,
        IReadOnlyList<int> blockedTriangles,
        Func<int, bool> releasingThisWouldRoute,
        out FieldNativeDoorOpeningLine opening)
    {
        ArgumentNullException.ThrowIfNull(releasingThisWouldRoute);
        for (var index = 0; index < blockedTriangles.Count; index++)
        {
            if (TryFind(fieldId, blockedTriangles[index], out var candidate) &&
                releasingThisWouldRoute(candidate.LockedTriangle))
            {
                opening = candidate;
                return true;
            }
        }

        opening = default;
        return false;
    }

    /// <summary>Every known row, for tests and diagnostics.</summary>
    public static IReadOnlyList<FieldNativeDoorOpeningLine> All => Known;
}
