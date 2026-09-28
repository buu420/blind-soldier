using Ff7.Accessibility.LegacyLayout;

namespace Ff7.Accessibility.Reloaded;

/// <summary>
/// Doors between two rooms of one field. The game draws them as doors and a sighted player
/// walks through them, but no gateway, LINE or MAPJUMP is involved - the rooms share one
/// walkmesh and the door is a native triangle lock - so nothing else gives them an Exit.
///
/// <para>sininb2 (303), the Shinra Mansion basement, is the reported case. The coffin room
/// holding Vincent sits behind triangle 34. The Director's Init runs
/// <c>IFUB B1[232] !bit 1</c> then <c>IDLCK 34 lock</c> (6D220001), and sinin2_1's safe sets
/// <c>B1[232] bit 1</c> when it hands over the Key to the Basement. So the door is locked on every
/// entry until the key is held and open on every entry after. Triangle 34 joins 6 on the
/// passage side and 35 in the room, and with 34 closed the 80 room triangles below are reachable
/// from nothing else (triangles 114 and 115 are an unconnected z 21 island). Both installed
/// archives have the same walkmesh and the same Init.</para>
///
/// <para>Which of the two is offered is the side the leader stands on; on the threshold, both.
/// Each completes only on entering the far side's walkmesh region, so a sample can pass more
/// than one triangle beyond the threshold. Whether the door is open is never read here. Route planning honours
/// the live IDLCK bit, so before the key the exit stays listed and is explained as shut, and an
/// unreadable boundary publishes nothing.</para>
/// </summary>
public static class FieldRoomDoorExitCatalog
{
    private sealed record RoomDoor(
        int FieldId,
        int DoorTriangle,
        IReadOnlySet<int> RoomTriangles,
        IReadOnlySet<int> PassageTriangles,
        FieldNavigationTarget Enter,
        FieldNavigationTarget Leave);

    // The 80 room triangles are what triangle 34 closes off from 35; the 33 passage triangles are
    // what it closes off from the passage arrival (12). Each way through the door is finished by
    // standing anywhere on its far side, so a sample that carries the party more than one
    // triangle past the threshold still completes it.
    private static readonly int[] SininbRoom =
        [25, 26, 27, 30, 35, .. Enumerable.Range(38, 110 - 38 + 1), 112, 113];

    private static readonly int[] SininbPassage =
        [.. Enumerable.Range(0, 25), 28, 29, 31, 32, 33, 36, 37, 111];

    private static readonly RoomDoor[] Doors =
    [
        new(
            303,
            34,
            SininbRoom.ToHashSet(),
            SininbPassage.ToHashSet(),
            // Triangle 35's centroid, just past the threshold's room edge 240,-538 - 231,-641.
            Door(303, 34, "enter", "Enter the coffin room", 250, -587, SininbRoom),
            // Triangle 6's centroid, just past the threshold's passage edge 184,-596 - 240,-538.
            Door(303, 34, "leave", "Leave the coffin room", 216, -513, SininbPassage))
    ];

    /// <summary>The door exits the leader's side of each door offers.</summary>
    public static IReadOnlyList<FieldNavigationTarget> ForPosition(FieldPositionSnapshot position)
    {
        if (!FieldPositionReader.IsUsable(position))
        {
            return Array.Empty<FieldNavigationTarget>();
        }

        var targets = new List<FieldNavigationTarget>();
        foreach (var door in Doors)
        {
            if (door.FieldId != position.FieldId)
            {
                continue;
            }

            // Offered strictly by side. The walk already under way through the door is kept by
            // the controller while the party stands on that target's own completion triangles,
            // so it finishes there rather than being cancelled as the offer changes.
            var triangle = position.TriangleId;
            var inRoom = door.RoomTriangles.Contains(triangle);
            if (triangle == door.DoorTriangle || !inRoom)
            {
                targets.Add(door.Enter);
            }

            if (triangle == door.DoorTriangle || inRoom)
            {
                targets.Add(door.Leave);
            }
        }

        return targets;
    }

    /// <summary>
    /// A published exit list with the door exits of the leader's side added. Appended after
    /// the host's settle gate: a door's exits change as the leader crosses it, and that must not
    /// make the gate hold back every other exit while it settles again.
    /// </summary>
    public static IReadOnlyList<FieldNavigationTarget> Append(
        IReadOnlyList<FieldNavigationTarget> published, FieldPositionSnapshot position)
    {
        var doors = ForPosition(position);
        return doors.Count == 0 ? published : published.Concat(doors).DistinctBy(target => target.StableId).ToArray();
    }

    /// <summary>The room triangles behind a door, for the installed-archive checks.</summary>
    internal static IReadOnlySet<int>? RoomTriangles(int fieldId, int doorTriangle) =>
        Doors.FirstOrDefault(door => door.FieldId == fieldId && door.DoorTriangle == doorTriangle)?.RoomTriangles;

    /// <summary>The passage triangles on the other side of a door, for the installed-archive checks.</summary>
    internal static IReadOnlySet<int>? PassageTriangles(int fieldId, int doorTriangle) =>
        Doors.FirstOrDefault(door => door.FieldId == fieldId && door.DoorTriangle == doorTriangle)?.PassageTriangles;

    private static FieldNavigationTarget Door(
        int fieldId, int doorTriangle, string direction, string label, int x, int y, int[] farSide) =>
        new(fieldId, FieldNavigationCategory.Exits, label, x, y, 0,
            $"room-door:{fieldId}:{doorTriangle}:{direction}",
            CompletesOnArrival: true,
            CompletionTriangles: farSide);
}
