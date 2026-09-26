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
    int LineActivationRadius = 0);

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
}
