namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// The Temple of the Ancients doorway chase, 610 kuro_7: three floors of doorways the guard
/// who took the key runs between, and the party chases him through. Every value here is the
/// field's own; ChaseAndExcavationTests checks each against both installed archives.
///
/// <list type="bullet">
/// <item><description>
/// <b>Doors.</b> border1..border9 are entities 6..14, each a LINE (script 0) whose Go 1x runs
/// one of cloud's scripts 6..14. Unless the guard is caught there, that script hides the party,
/// plays the guard's run (keyman script 4) and places the party at another door's mouth
/// (placeObject), from where it walks out. Every door is led into by exactly one other, so each
/// door has one mouth: the point the party is placed at when it comes out of that door.
/// </description></item>
/// <item><description>
/// <b>Floors.</b> The upper floor is at about z 370, with one door, the ledge jump1 (entity 16),
/// the locked doorway mapjump (entity 15) and the doorway from the clock room (the field's one
/// gateway). The middle floor is at about z 0, with four doors and the ledge jump2 (entity 17).
/// The lower floor is at about z -390, with four doors. The ledges only go down.
/// </description></item>
/// <item><description>
/// <b>Numbering.</b> The field's camera x axis is (4095, -15, 0): the screen's left to right is
/// the field's x. Doors are numbered from the left on each floor.
/// </description></item>
/// </list>
///
/// <para>The door the guard will come out of next is Bank[5][18]. That is the puzzle - the
/// game's own hint is to memorise the doors he enters and exits - and nothing here reads it.
/// What is said of him is where he is seen: which door he comes out of and goes into.</para>
/// </summary>
public static class TempleChaseLayout
{
    public const int FieldId = 610;

    public enum ChaseFloor
    {
        Upper,
        Middle,
        Lower
    }

    /// <param name="MouthX">Where the party is placed coming out of this door (the placeObject of the door that leads to it).</param>
    /// <param name="LeadsToEntityId">The door whose mouth this door's own cloud script places the party at.</param>
    public readonly record struct ChaseDoor(
        int EntityId,
        ChaseFloor Floor,
        int Number,
        string Label,
        FieldNavigationTriggerLine Line,
        int MouthX,
        int MouthY,
        int MouthZ,
        int LeadsToEntityId);

    /// <summary>An [OK] line whose Go runs cloud's makeObjectJump down one floor.</summary>
    public readonly record struct ChaseLedge(
        int EntityId,
        ChaseFloor From,
        ChaseFloor To,
        string Label,
        FieldNavigationTriggerLine Line);

    /// <summary>A doorway the party cannot go through from here, but the guard can come out of or go into.</summary>
    public readonly record struct ChasePlace(
        string Name,
        ChaseFloor Floor,
        FieldNavigationTriggerLine Line,
        int MouthX,
        int MouthY);

    public static readonly IReadOnlyList<ChaseDoor> Doors =
    [
        new(6, ChaseFloor.Upper, 1, "Upper floor door", new(-669, 788, 370, -541, 788, 368), -610, 598, 365, 9),
        new(7, ChaseFloor.Middle, 1, "Middle floor door 1", new(-760, 453, 0, -616, 453, 0), -707, 303, -5, 12),
        new(8, ChaseFloor.Middle, 2, "Middle floor door 2", new(-304, 427, -9, -168, 424, -9), -224, 202, -2, 14),
        new(9, ChaseFloor.Middle, 3, "Middle floor door 3", new(72, 344, 0, 216, 344, 0), 139, 189, -8, 7),
        new(10, ChaseFloor.Middle, 4, "Middle floor door 4", new(458, 488, -44, 594, 488, -44), 551, 318, -41, 11),
        new(11, ChaseFloor.Lower, 1, "Lower floor door 1", new(-652, 298, -393, -512, 298, -393), -608, 108, -393, 8),
        new(12, ChaseFloor.Lower, 2, "Lower floor door 2", new(-290, 223, -396, -154, 223, -396), -221, -29, -389, 13),
        new(13, ChaseFloor.Lower, 3, "Lower floor door 3", new(114, 189, -390, 238, 189, -391), 179, 22, -396, 10),
        new(14, ChaseFloor.Lower, 4, "Lower floor door 4", new(463, 135, -436, 601, 203, -437), 566, 3, -433, 6),
    ];

    public static readonly IReadOnlyList<ChaseLedge> Ledges =
    [
        new(16, ChaseFloor.Upper, ChaseFloor.Middle, "Ledge down to the middle floor; press OK",
            new(-675, 223, 370, -487, 177, 369)),
        new(17, ChaseFloor.Middle, ChaseFloor.Lower, "Ledge down to the lower floor; press OK",
            new(-665, 79, -9, -562, 45, -10)),
    ];

    /// <summary>
    /// The upper floor's other two doorways. The guard comes out of the one from the clock room
    /// (gateway0; its arrival point is the mouth) in keyman script 4, and flees through the middle
    /// one (mapjump, locked by triangle 101 until he is caught) in keyman script 3.
    /// </summary>
    public static readonly IReadOnlyList<ChasePlace> OtherDoorways =
    [
        new("the doorway from the clock room", ChaseFloor.Upper, new(421, 791, 323, 552, 850, 323), 508, 535),
        new("the doorway in the middle of the upper floor", ChaseFloor.Upper, new(40, 417, 423, -92, 417, 423), -21, 267),
    ];

    /// <summary>How far from a doorway's mouth-to-line segment a position still counts as at it.</summary>
    public const int DoorwayReach = 160;

    public static ChaseFloor FloorOf(int z) => z switch
    {
        >= 200 => ChaseFloor.Upper,
        <= -200 => ChaseFloor.Lower,
        _ => ChaseFloor.Middle
    };

    public static string FloorName(ChaseFloor floor) => floor switch
    {
        ChaseFloor.Upper => "upper floor",
        ChaseFloor.Middle => "middle floor",
        _ => "lower floor"
    };

    /// <summary>The door or doorway a position is at, spoken as a place ("middle floor door 2"), or null when it is at none.</summary>
    public static string? PlaceAt(int x, int y, int z)
    {
        var floor = FloorOf(z);
        string? best = null;
        var bestDistance = double.MaxValue;
        foreach (var door in Doors.Where(door => door.Floor == floor))
        {
            Consider(DoorName(door), door.Line, door.MouthX, door.MouthY);
        }

        foreach (var place in OtherDoorways.Where(place => place.Floor == floor))
        {
            Consider(place.Name, place.Line, place.MouthX, place.MouthY);
        }

        return bestDistance <= DoorwayReach ? best : null;

        void Consider(string name, FieldNavigationTriggerLine line, int mouthX, int mouthY)
        {
            var distance = DistanceToSegment(x, y, mouthX, mouthY, (line.StartX + line.EndX) / 2d, (line.StartY + line.EndY) / 2d);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = name;
            }
        }
    }

    /// <summary>The door a position is at, when it is at one of the nine the party can go through.</summary>
    public static ChaseDoor? DoorAt(int x, int y, int z)
    {
        var place = PlaceAt(x, y, z);
        return Doors.FirstOrDefault(door => DoorName(door) == place) is { EntityId: > 0 } door ? door : null;
    }

    /// <summary>A door as a place in a sentence: "the upper floor door", "middle floor door 2".</summary>
    public static string DoorName(ChaseDoor door) =>
        door.Floor == ChaseFloor.Upper ? "the upper floor door" : door.Label.ToLowerInvariant();

    /// <summary>
    /// The doors and ledges as Exits, each only where the installed field's own LINE is exactly
    /// the one recorded here: a door is crossed, and done once it is (the party is then placed
    /// elsewhere by the door's own script); a ledge waits for [OK].
    /// </summary>
    public static IReadOnlyList<FieldNavigationTarget> ExitsFor(
        int fieldId,
        IReadOnlyDictionary<int, FieldNavigationTriggerLine> nativeLines)
    {
        if (fieldId != FieldId)
        {
            return Array.Empty<FieldNavigationTarget>();
        }

        var exits = new List<FieldNavigationTarget>();
        foreach (var door in Doors)
        {
            if (nativeLines.TryGetValue(door.EntityId, out var line) && line == door.Line)
            {
                exits.Add(Exit($"chase-door:{FieldId}:{door.EntityId}", door.Label, door.EntityId, line));
            }
        }

        foreach (var ledge in Ledges)
        {
            if (nativeLines.TryGetValue(ledge.EntityId, out var line) && line == ledge.Line)
            {
                // Its label says OK; the field activity readout names the press on the ledge itself.
                // Manual guidance would make it a hint the controller never routes to.
                exits.Add(Exit($"chase-ledge:{FieldId}:{ledge.EntityId}", ledge.Label, ledge.EntityId, line));
            }
        }

        return exits;
    }

    /// <summary>The label an exit of this layout keeps, or null for any other target.</summary>
    public static string? ResolveLabel(FieldNavigationTarget target) =>
        target.FieldId == FieldId &&
        (target.StableId.StartsWith("chase-door:", StringComparison.Ordinal) ||
         target.StableId.StartsWith("chase-ledge:", StringComparison.Ordinal))
            ? Doors.Where(door => door.EntityId == target.TriggerEntityId).Select(door => door.Label)
                .Concat(Ledges.Where(ledge => ledge.EntityId == target.TriggerEntityId).Select(ledge => ledge.Label))
                .FirstOrDefault()
            : null;

    private static FieldNavigationTarget Exit(string stableId, string label, int entityId, FieldNavigationTriggerLine line) =>
        new(FieldId, FieldNavigationCategory.Exits, label,
            (line.StartX + line.EndX) / 2, (line.StartY + line.EndY) / 2, (line.StartZ + line.EndZ) / 2,
            stableId,
            TriggerEntityId: entityId,
            CompletesOnArrival: true,
            DestinationFieldIds: [FieldId],
            TriggerLine: line);

    private static double DistanceToSegment(double x, double y, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var lengthSquared = dx * dx + dy * dy;
        var t = lengthSquared <= 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / lengthSquared, 0, 1);
        var px = ax + dx * t - x;
        var py = ay + dy * t - y;
        return Math.Sqrt(px * px + py * py);
    }
}
