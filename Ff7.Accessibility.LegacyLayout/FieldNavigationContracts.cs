namespace Ff7.Accessibility.Reloaded;

public enum FieldNavigationCategory
{
    Exits,
    Story,
    Npcs,
    Objects
}

/// <summary>
/// How the native field engine turns standing next to something into the thing
/// happening. Talk is a Confirm press against the model's own talk radius; Contact
/// is the collision test FUN_00637724 runs while the party is walking, and fires
/// from walking into the model rather than from any button at all.
/// </summary>
public enum FieldNavigationActivation
{
    Default,
    Talk,
    Contact
}

public enum FieldObjectCueKind
{
    None,
    Materia,
    Chest,
    Item
}

public readonly record struct FieldNavigationTriggerLine(
    int StartX,
    int StartY,
    int StartZ,
    int EndX,
    int EndY,
    int EndZ);

public readonly record struct FieldNavigationRouteDetour(
    FieldNavigationTriggerLine BlockedLine,
    int X,
    int Y,
    int Z,
    int Clearance = 0);

public readonly record struct FieldNavigationTarget(
    int FieldId,
    FieldNavigationCategory Category,
    string Label,
    int X,
    int Y,
    int Z,
    string StableId = "",
    FieldObjectCueKind ObjectCueKind = FieldObjectCueKind.None,
    int TriggerEntityId = -1,
    bool CompletesOnArrival = false,
    int InteractionRadius = 0,
    IReadOnlyList<int>? DestinationFieldIds = null,
    FieldNavigationTriggerLine? TriggerLine = null,
    FieldNavigationRouteDetour? RouteDetour = null,
    IReadOnlyList<FieldNavigationRouteDetour>? RouteDetours = null,
    string? ManualNavigationGuidance = null,
    // Some native activations are neither a trigger line nor a gateway: the script
    // polls the party leader's walkmesh triangle and fires when it enters a set of
    // triangles. Those targets are reached only by standing on one of them, so a
    // proximity radius can release auto walk while the player is still outside.
    IReadOnlyList<int>? CompletionTriangles = null,
    FieldNavigationActivation Activation = FieldNavigationActivation.Default,
    // A point just past a line that does its work only when crossed (the Ancient Forest's
    // throwing zones). The object that offers it is withheld by where the player stands, so it
    // goes away as the walk crosses the line; the controller keeps the locked point, and only
    // inside its own approach (see FieldNavigationController.IsInsideCrossingApproach).
    FieldNavigationTriggerLine? ApproachCrossingLine = null,
    // A target the game activates by the leader touching a LINE: TriggerLine is that line's
    // live segment and this the leader's own collision radius (event +0x72). The engine counts
    // the leader as on the line only when FieldNativeLineContact.Touches says so (00637ABB with
    // 00637879), so arrival is measured that way and not by the player's arrival distance.
    int LineActivationRadius = 0,
    // That line runs its script on OK (00637D35), which also needs the leader facing it
    // (FieldNativeLineContact.AcceptsOk); a Go line runs on the touch alone.
    bool LineActivatesOnOk = false,
    // The engine's own OK verdict for that line on this read, when the reader has it: the LINE's
    // ready byte (+0x15) and stored angle (+0x14) against the leader's event heading (+0x36), as
    // 00637D35 tests them. Null when it could not be read.
    bool? LineOkAccepted = null);

/// <summary>A LINE's native OK state: the stored angle to its foot (+0x14) and the ready byte (+0x15).</summary>
public readonly record struct FieldNavigationLineOkState(byte Angle, bool Ready);

/// <summary>
/// The engine's own test of the party leader touching a LINE. 00637879 projects the leader's
/// position onto the line in 1/256 steps, gives -1 when the foot of the projection lies outside
/// the segment's X or Y extent, and otherwise the squared three-dimensional distance to it;
/// 00637ABB then counts the leader as on the line only while that is strictly less than the
/// square of the leader's collision radius (event +0x72). Integer arithmetic throughout, as there.
/// </summary>
public static class FieldNativeLineContact
{
    /// <summary>The squared distance 00637879 returns, or -1 off the segment.</summary>
    public static long DistanceSquared(FieldNavigationTriggerLine line, int x, int y, int z)
    {
        // 32-bit and unchecked, as the engine computes it.
        unchecked
        {
            int x0 = line.StartX, y0 = line.StartY, z0 = line.StartZ;
            int x1 = line.EndX, y1 = line.EndY, z1 = line.EndZ;
            var lengthSquared = (x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0) + (z1 - z0) * (z1 - z0);
            if (lengthSquared == 0)
            {
                return -1;
            }

            var numerator = ((x0 - x) * (x1 - x0) + (y0 - y) * (y1 - y0) + (z0 - z) * (z1 - z0)) * -0x100;
            if (lengthSquared == -1 && numerator == int.MinValue)
            {
                // The one division that throws even unchecked; a snapshot this malformed is no touch.
                return -1;
            }

            var t = numerator / lengthSquared;
            var footX = (t * (x1 - x0) >> 8) + x0;
            var footY = (t * (y1 - y0) >> 8) + y0;
            var footZ = (t * (z1 - z0) >> 8) + z0;
            var outsideX = (x0 - footX < 0 || (x1 != footX && -1 < x1 - footX)) &&
                           ((x0 != footX && -1 < x0 - footX) || x1 - footX < 0);
            var outsideY = (y0 - footY < 0 || (y1 != footY && -1 < y1 - footY)) &&
                           ((y0 != footY && -1 < y0 - footY) || y1 - footY < 0);
            if (outsideX || outsideY)
            {
                return -1;
            }

            return (footX - x) * (footX - x) + (footY - y) * (footY - y) + (footZ - z) * (footZ - z);
        }
    }

    /// <summary>
    /// The leader's position as 00637ABB takes it: the event block's fixed-point X, Y and Z
    /// (0x00CC1670 + index * 0x88, +0x0C to +0x14) shifted right by twelve, which floors. A
    /// snapshot read from there carries them in NativeFixedPosition; one without them (a test, a
    /// rendered position) gives its integer coordinates.
    /// </summary>
    public static (int X, int Y, int Z) NativeCoordinates(FieldPositionSnapshot position) =>
        position.NativeFixedPosition is { } fixedPosition
            ? (fixedPosition.X >> 12, fixedPosition.Y >> 12, fixedPosition.Z >> 12)
            : (position.X, position.Y, position.Z);

    /// <summary>Whether the leader in this snapshot, with this collision radius, is on the line.</summary>
    public static bool Touches(FieldNavigationTriggerLine line, FieldPositionSnapshot position, int collisionRadius)
    {
        var (x, y, z) = NativeCoordinates(position);
        return Touches(line, x, y, z, collisionRadius);
    }

    /// <summary>Whether the leader at (x, y, z) with this collision radius is on the line.</summary>
    public static bool Touches(FieldNavigationTriggerLine line, int x, int y, int z, int collisionRadius)
    {
        var distance = DistanceSquared(line, x, y, z);
        return collisionRadius > 0 && distance >= 0 && distance < (long)collisionRadius * collisionRadius;
    }

    /// <summary>
    /// Whether OK pressed now would run this line's OK script, computed from the geometry. The
    /// assistant does not use it: auto walk arrives only on the engine's own verdict
    /// (<see cref="FieldNavigationTarget.LineOkAccepted"/>), and an unreadable one is unproven.
    /// This is the reference rule for tests and diagnostics that have no game memory.
    /// 00637ABB sets the LINE's ready byte (+0x15) while the leader touches it and faces within 64
    /// units of the angle to its nearest foot (00636515, stored in +0x14); 00637D35 then runs the
    /// OK script only when <c>(angle - heading + 0x20) &amp; 0xFF &lt; 0x40</c>, heading being the
    /// leader's event +0x36. Here the angle is <see cref="NativeAngle"/> (the installed table, not
    /// atan2) and the heading is the snapshot's direction - the rendered +0x38, which 006342C6
    /// copies from +0x36, so the two can differ mid-turn.
    /// Standing exactly on the foot, 00637ABB keeps the angle stored on an earlier frame and
    /// 00637D35 still tests it; with no stored angle to hand nothing is proven, so it is false.
    /// </summary>
    public static bool AcceptsOk(FieldNavigationTriggerLine line, FieldPositionSnapshot position, int collisionRadius)
    {
        if (!Touches(line, position, collisionRadius))
        {
            return false;
        }

        var (x, y, z) = NativeCoordinates(position);
        if (!TryFoot(line, x, y, z, out var footX, out var footY) || (footX == x && footY == y))
        {
            return false;
        }

        var angle = NativeAngle(footX - x, footY - y);
        return angle >= 0 &&
               ((angle - position.Direction + 0x40) & 0xFF) < 0x80 &&
               ((angle - position.Direction + 0x20) & 0xFF) < 0x40;
    }

    /// <summary>The engine's OK sector test (00637D35) for a stored angle and an event heading.</summary>
    public static bool IsInOkSector(byte storedAngle, byte eventHeading) =>
        ((storedAngle - eventHeading + 0x20) & 0xFF) < 0x40;

    /// <summary>
    /// 00636515's angle of (dx, dy): the length by integer square root, each component scaled to
    /// 4096 over the length and divided by 32 toward zero, then looked up in the table at
    /// 009067C0 (the installed ff7_en.exe's, below), less 0xC0. East is 64 and south 128, the
    /// movement heading's own convention. -1 for a zero vector, which the engine never asks.
    /// </summary>
    public static int NativeAngle(int dx, int dy)
    {
        var length = (int)Math.Sqrt((double)dx * dx + (double)dy * dy);
        while ((long)(length + 1) * (length + 1) <= (long)dx * dx + (long)dy * dy)
        {
            length++;
        }

        while ((long)length * length > (long)dx * dx + (long)dy * dy)
        {
            length--;
        }

        if (length == 0)
        {
            return -1;
        }

        var sx = dx * 4096 / length / 32;
        var sy = dy * 4096 / length / 32;
        int value;
        if (sx * sx <= sy * sy)
        {
            value = sy <= 0
                ? sx <= 0 ? 0xC0 - AngleTable[-sx] : -(0x40 - AngleTable[sx])
                : sx <= 0 ? 0x80 - (0x40 - AngleTable[-sx]) : 0x40 - AngleTable[sx];
        }
        else if (sx <= 0)
        {
            value = sy <= 0 ? AngleTable[-sy] + 0x80 : 0x80 - AngleTable[sy];
        }
        else
        {
            value = sy <= 0 ? -AngleTable[-sy] : AngleTable[sy];
        }

        return ((value & 0xFF) - 0xC0) & 0xFF;
    }

    /// <summary>The 129 words at 009067C0 in the installed ff7_en.exe (checked against it with installed data).</summary>
    public static readonly IReadOnlyList<ushort> AngleTable =
    [
        0, 0, 1, 1, 1, 2, 2, 2, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 6, 6, 6, 7, 7, 7, 8, 8, 8, 9, 9, 9, 10, 10,
        10, 11, 11, 11, 12, 12, 12, 13, 13, 13, 14, 14, 14, 15, 15, 15, 16, 16, 16, 17, 17, 17, 18, 18, 18,
        19, 19, 20, 20, 20, 21, 21, 21, 22, 22, 22, 23, 23, 24, 24, 24, 25, 25, 26, 26, 26, 27, 27, 28, 28,
        28, 29, 29, 30, 30, 30, 31, 31, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32,
        32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32
    ];
    /// <summary>The foot of the leader's projection on the line, as 00637879 computes it; false off the segment.</summary>
    public static bool TryFoot(FieldNavigationTriggerLine line, int x, int y, int z, out int footX, out int footY)
    {
        footX = x;
        footY = y;
        if (DistanceSquared(line, x, y, z) < 0)
        {
            return false;
        }

        unchecked
        {
            int x0 = line.StartX, y0 = line.StartY, z0 = line.StartZ;
            int x1 = line.EndX, y1 = line.EndY, z1 = line.EndZ;
            var lengthSquared = (x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0) + (z1 - z0) * (z1 - z0);
            var t = ((x0 - x) * (x1 - x0) + (y0 - y) * (y1 - y0) + (z0 - z) * (z1 - z0)) * -0x100 / lengthSquared;
            footX = (t * (x1 - x0) >> 8) + x0;
            footY = (t * (y1 - y0) >> 8) + y0;
        }

        return true;
    }
}
